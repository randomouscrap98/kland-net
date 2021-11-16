using System.Text.RegularExpressions;
using Amazon.S3;
using kland.Db;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;

namespace kland.Controllers;

public class KlandImageHostControllerConfig
{
    public string? IdRegex {get;set;}
    public string? AIdRegex {get;set;}
    public string? ETagPrepend {get;set;}
    public string? ShortHost {get;set;}
    public string? RawImageFormat {get;set;}
    public int HashLength {get;set;}
    public int MaxImageSize {get;set;}
    public int MaxHashRetries {get;set;}
    public string? IpHeader {get;set;}

    public TimeSpan MaxHashLockWait {get;set;} = TimeSpan.FromSeconds(30);
}

[ApiController]
[Route("")] //Default route goes here too?
public class KlandImageHostController: KlandBase
{
    protected IUploadStore uploadStore;
    protected KlandImageHostControllerConfig config;
    protected Regex idRegex;
    protected Regex aidRegex;
    protected Regex rawImgRegex;

    public static readonly object ImageHashLock = new object();
    public static readonly object ThreadHashLock = new object();

    public KlandImageHostController(ILogger<KlandController> logger, KlandDbContext dbContext, IAmazonS3 s3client,
        KlandImageHostControllerConfig config, IUploadStore uploadStore) : base(logger, dbContext)
    {
        this.config = config;
        this.uploadStore = uploadStore;

        idRegex = new Regex(config.IdRegex ?? throw new InvalidOperationException("No image ID regex!"), RegexOptions.IgnoreCase);
        aidRegex = new Regex(config.AIdRegex ?? throw new InvalidOperationException("No alt ID regex!"), RegexOptions.IgnoreCase);
        rawImgRegex = new Regex(config.RawImageFormat ?? throw new InvalidOperationException("No raw image regex!"), RegexOptions.IgnoreCase);
    }

    protected async Task<IActionResult> GetGeneralFile(string id, Regex regex, Func<Match, string> mimeGenerator)
    {
        var match = regex.Match(id);

        if(!match.Success)
            return BadRequest($"S3 ID not in specified format: {regex.ToString()}");

        //Go get it from s3
        try
        {
            var data = await uploadStore.GetDataAsync(id);
            Response.Headers.Add("ETag", config.ETagPrepend + id);
            return File(data, mimeGenerator(match));
        }
        catch(Exception ex)
        {
            logger.LogError($"Exception during general file request: {ex}");
            return NotFound();
        }
    }

    //This BLOCKS, could bog down the server requests?
    protected async Task<string> GetNewThreadHashAsync(int hashLength)
    {
        if(Monitor.TryEnter(ThreadHashLock, config.MaxHashLockWait))
        {
            try
            {
                string hash = "";

                do
                {
                    hash = GetRandomAlphaString(hashLength);
                } while (await dbContext.Threads.FirstOrDefaultAsync(x => x.hash == hash) != null);

                return hash;
            }
            finally
            {
                Monitor.Exit(ThreadHashLock);
            }
        }
        else
        {
            throw new InvalidOperationException($"Couldn't acquire thread hash lock! Max wait: {config.MaxHashLockWait}");
        }
    }

    protected async Task<kland.Db.Thread> GetOrCreateBucketThread(string? bucket)
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

        if(string.IsNullOrEmpty(thread.hash))
        {
            logger.LogDebug($"Thread {subject} has no hash, generating now");
            thread.hash = await GetNewThreadHashAsync(config.HashLength);
            await dbContext.SaveChangesAsync();
            logger.LogInformation($"Generated hash {thread.hash} for thread {thread.subject}");
        }

        return thread;
    }

    [HttpGet("i/{id}")]
    [ResponseCache(Duration = 13824000)] //six months
    public Task<IActionResult> GetImageAsync([FromRoute] string id)
    {
        return GetGeneralFile(id, idRegex, m => "image/" + m.Groups[1].Value?.TrimStart('.')); 
    }

    [HttpGet("a/{id}")]
    [ResponseCache(Duration = 13824000)] //six months
    public Task<IActionResult> GetTextAsync([FromRoute] string id)
    {
        return GetGeneralFile(id, aidRegex, m => "text/plain");
    }

    [HttpPost("uploadtext")]
    public IActionResult PostText()
    {
        return BadRequest("Sorry, the animation uploader is not implemented anymore! If it's needed, it can be added again!");
    }

    public class KlandImageData
    {
        public byte[] data {get;set;} = new byte[0];
        public string mimetype {get;set;} = "";
    }

    protected KlandImageData ParseRawImageString(string raw)
    {
        var match = rawImgRegex.Match(raw);

        if(!match.Success)
            throw new InvalidOperationException($"Bad raw image format! Needs to be: {config.RawImageFormat}");
        
        return new KlandImageData()
        {
            mimetype = match.Groups[1].Value,
            data = Convert.FromBase64String(match.Groups[2].Value)
        };
    }

    [HttpPost("uploadimage")]
    public async Task<ActionResult<string>> UploadImage([FromForm]IFormFile? image, [FromForm]string? animation, [FromForm]string? raw,
        [FromForm]string? redirect, [FromForm]string? url, [FromForm]string? bucket, [FromForm]string? shorturl)
    {
        //Image is the "standard" form upload, but sometimes users can submit "raw" images in the standard base64 
        //blob format that you can insert into image src.
        //Redirect is just that: the url to redirect to?
        bool realRedirect = StringToBool(redirect);
        bool realShort = StringToBool(shorturl);
        string imageUrl = "";
        string finalImageName = "";
        var ipaddress = Request.Headers[config.IpHeader ?? throw new InvalidOperationException("No header field for ip tracking set!")]
            .FirstOrDefault() ?? "unknown";

        //url is part of an admin task and can be omitted for now
        if(!string.IsNullOrEmpty(url))
            return BadRequest("Admin tasks are not currently implemented. They can be if needed!");
        
        //Before anything, let's get the thread to put the image into!
        kland.Db.Thread? bucketThread = null;

        //This won't be the user's fault if this fails, so let internal server errors happen
        bucketThread = await GetOrCreateBucketThread(bucket);

        KlandImageData imageData = new KlandImageData();

        //A REAL image was given in the form! This is hopefully the default
        if(!string.IsNullOrWhiteSpace(image?.FileName))
        {
            using(var stream = new MemoryStream())
            {
                await image.CopyToAsync(stream);
                imageData.data = stream.ToArray(); //regardless of position
            }
        }
        //Ah, they gave us raw data. That's ok too
        else if(!string.IsNullOrEmpty(raw))
        {
            try
            {
                //Note: even though we retrieve a full image data, we ignore the mimetype given by the users.
                imageData = ParseRawImageString(raw);
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

        if(imageData.data.Length > config.MaxImageSize)
            return BadRequest($"Image too large! Max size: {config.MaxImageSize}");
        else if(imageData.data.Length <= 0)
            return BadRequest($"No image seems to have been given! Length 0!");

        string extension = "";

        try
        {
            IImageFormat format;

            //This is JUST to see if it's an image (and get the format)
            using (var img = Image.Load(imageData.data, out format))
            {
                extension = format.FileExtensions.First();
                imageData.mimetype = format.DefaultMimeType;
            }
        }
        catch(Exception ex)
        {
            logger.LogWarning($"Couldn't test image: {ex}");
            return BadRequest("Couldn't parse image data into image!");
        }

        //And NOW we upload? We tell the uploader how to generate names if there are collisions (this is how kland works),
        //and some uploaders require the mimetype, so give that too
        finalImageName = await uploadStore.PutDataAsync(imageData.data, 
            () => $"{GetRandomAlphaString(config.HashLength)}.{extension}", imageData.mimetype);
        
        var post = new Post()
        {
            content = "orphanedImage",
            ipaddress = ipaddress,
            image = finalImageName,
            tid = bucketThread.tid
        };

        dbContext.Posts.Add(post);
        await dbContext.SaveChangesAsync();

        if(realShort)
            imageUrl = $"{config.ShortHost}/{finalImageName}";
        else
            imageUrl = $"{Request.Scheme}://{Request.Host}/i/{finalImageName}";
        
        if(realRedirect)
            return Redirect(imageUrl);
        else
            return imageUrl;
    }
}
