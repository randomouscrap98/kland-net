using System.Text.RegularExpressions;
using Amazon.S3;
using kland.Db;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace kland.Controllers;

public class KlandImageHostControllerConfig
{
    public string? Bucket {get;set;}
    public string? IdRegex {get;set;}
    public string? AIdRegex {get;set;}
    public string? ETagPrepend {get;set;}
    public string? ShortHost {get;set;}
    public string? RawImageFormat {get;set;}
    public int MaxImageSize {get;set;}
}

[ApiController]
[Route("")] //Default route goes here too?
public class KlandImageHostController: KlandBase
{
    protected IAmazonS3 s3client;
    protected KlandImageHostControllerConfig config;
    protected Regex idRegex;
    protected Regex aidRegex;
    protected Regex rawImgRegex;

    public static readonly object ImageHashLock = new object();
    public static readonly object ThreadHashLock = new object();

    public KlandImageHostController(ILogger<KlandController> logger, KlandDbContext dbContext, IAmazonS3 s3client,
        KlandImageHostControllerConfig config) : base(logger, dbContext)
    {
        this.s3client = s3client;
        this.config = config;

        //Just don't even bother if the config has no bucket. We want to immediately know when this is broken,
        //so it's ok to break the entire kland for this!
        if(string.IsNullOrWhiteSpace(config.Bucket))
            throw new InvalidOperationException("No bucket set for images!");

        idRegex = new Regex(config.IdRegex ?? throw new InvalidOperationException("No image ID regex!"), RegexOptions.IgnoreCase);
        aidRegex = new Regex(config.AIdRegex ?? throw new InvalidOperationException("No alt ID regex!"), RegexOptions.IgnoreCase);
        rawImgRegex = new Regex(config.RawImageFormat ?? throw new InvalidOperationException("No raw image regex!"), RegexOptions.IgnoreCase);
    }

    protected async Task<IActionResult> GetS3(string id, Regex regex, Func<Match, string> mimeGenerator)
    {
        var match = regex.Match(id);

        if(!match.Success)
            return BadRequest($"S3 ID not in specified format: {regex.ToString()}");

        //Go get it from s3
        try
        {
            var obj = await s3client.GetObjectAsync(config.Bucket, id);
            Response.Headers.Add("ETag", config.ETagPrepend + id);
            return File(obj.ResponseStream, mimeGenerator(match));
            //"image/" + match.Groups[1].Value?.TrimStart('.')); 
        }
        catch(Exception ex)
        {
            logger.LogError($"Exception during S3 request: {ex}");
            return NotFound();
        }
    }

    [HttpGet("i/{id}")]
    [ResponseCache(Duration = 13824000)] //six months
    public Task<IActionResult> GetImageAsync([FromRoute] string id) //, [FromQuery] GetFileModify modify)
    {
        return GetS3(id, idRegex, m => "image/" + m.Groups[1].Value?.TrimStart('.')); 
    }

    [HttpGet("a/{id}")]
    [ResponseCache(Duration = 13824000)] //six months
    public Task<IActionResult> GetTextAsync([FromRoute] string id)
    {
        return GetS3(id, aidRegex, m => "text/plain");
    }

    [HttpPost("uploadtext")]
    public IActionResult PostText()
    {
        return BadRequest("Sorry, the animation uploader is not implemented anymore! If it's needed, it can be added again!");
    }

    public class KlandImageUploadData
    {
        public string? extension {get;set;}
        public byte[] data {get;set;} = new byte[0];
    }

    protected KlandImageUploadData ParseRawImageString(string raw)
    {
        var result = new KlandImageUploadData();
        var match = rawImgRegex.Match(raw);

        if(!match.Success)
            throw new InvalidOperationException($"Bad raw image format! Needs to be: {config.RawImageFormat}");
        
        result.extension = match.Groups[1].Value;
        result.data = Convert.FromBase64String(match.Groups[2].Value);

        return result;
    }

    protected async Task<kland.Db.Thread> GetOrCreateBucketThread(string bucket)
    {
        //Go find the thread first off
        var subject = OrphanedPrepend + (string.IsNullOrEmpty(bucket) ? "" : $"_{bucket}");

        var thread = await dbContext.Threads.Where(x => x.subject == subject).FirstOrDefaultAsync();

        if(thread == null)
        {
            logger.LogDebug($"Bucket thread '{subject}' not found, adding");
            thread = new kland.Db.Thread()
            {
                subject = subject,
                created = DateTime.Now,
                deleted = true
            };
            dbContext.Threads.Add(thread);
            await dbContext.SaveChangesAsync(); //this should put the id in the thread
            logger.LogInformation($"Added Bucket thread '{subject}'");
        }

        return thread;
    }

    [HttpPost("uploadimage")]
    public async Task<ActionResult<string>> UploadImage([FromForm]IFormFile image, [FromForm]string animation, [FromForm]string raw,
        [FromForm]string redirect, [FromForm]string url, [FromForm]string bucket, [FromForm]string shorturl)
    {
        //Image is the "standard" form upload, but sometimes users can submit "raw" images in the standard base64 
        //blob format that you can insert into image src.
        //Redirect is just that: the url to redirect to?
        bool realRedirect = StringToBool(redirect);
        bool realShort = StringToBool(shorturl);
        string imageUrl = "";
        string finalImageName = "";

        //url is part of an admin task and can be omitted for now
        if(!string.IsNullOrEmpty(url))
            return BadRequest("Admin tasks are not currently implemented. They can be if needed!");
        
        //Before anything, let's get the thread to put the image into!
        kland.Db.Thread? bucketThread = null;

        //This won't be the user's fault if this fails, so let internal server errors happen
        bucketThread = await GetOrCreateBucketThread(bucket);

        KlandImageUploadData data = new KlandImageUploadData();

        //A REAL image was given in the form! This is hopefully the default
        if(!string.IsNullOrWhiteSpace(image?.FileName))
        {
            data.extension = Path.GetExtension(image.FileName);

            using(var stream = new MemoryStream())
            {
                await image.CopyToAsync(stream);
                data.data = stream.ToArray(); //regardless of position
            }
        }
        //Ah, they gave us raw data. That's ok too
        else if(!string.IsNullOrEmpty(raw))
        {
            try
            {
                data = ParseRawImageString(raw);
            }
            catch(Exception ex)
            {
                logger.LogWarning($"Error parsing raw image string: {ex}");
                return BadRequest($"Error during upload: {ex.Message}");
            }
        }
        else if(!string.IsNullOrEmpty(animation))
        {
            return BadRequest("Can't handle animations yet!");
        }
        else
        {
            return BadRequest("Can't understand the request! Must provide either form image, raw, or animation");
        }

        if(data.data.Length > config.MaxImageSize)
            return BadRequest($"Image too large! Max size: {config.MaxImageSize}");
        else if(data.data.Length <= 0)
            return BadRequest($"No image seems to have been given! Length 0!");

        
        if(realShort)
            imageUrl = $"{config.ShortHost}/{finalImageName}";
        
        if(realRedirect)
            return Redirect(imageUrl);
        else
            return imageUrl;
    }
}
