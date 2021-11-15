using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Amazon.S3;
using kland.Db;
using kland.Interfaces;
using kland.Views;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace kland.Controllers;

public class KlandControllerConfig
{
    public string? StaticBase {get;set;}
    public string? Bucket {get;set;}
    public string? IdRegex {get;set;}
    public string? ETagPrepend {get;set;}
    public string AdminId {get;set;} = "PLEASECHANGE";
    public double CookieExpireHours {get;set;}
}

[ApiController]
[Route("")] //Default route goes here?
public class KlandController : ControllerBase
{
    public const string AdminIdKey = "adminId";
    public const string PostStyleKey = "postStyle";

    private readonly ILogger _logger;
    protected KlandDbContext dbContext;
    protected IAmazonS3 s3client;
    protected KlandControllerConfig config;
    protected Regex idRegex;
    protected IPageRenderer pageRenderer;

    public KlandController(ILogger<KlandController> logger, KlandDbContext dbContext, IAmazonS3 s3client,
        KlandControllerConfig config, IPageRenderer pageRenderer)
    {
        _logger = logger;
        this.dbContext = dbContext;
        this.s3client = s3client;
        this.config = config;
        this.pageRenderer = pageRenderer;

        //Just don't even bother if the config has no bucket. We want to immediately know when this is broken,
        //so it's ok to break the entire kland for this!
        if(string.IsNullOrWhiteSpace(config.Bucket))
            throw new InvalidOperationException("No bucket set for images!");

        idRegex = new Regex(config.IdRegex ?? throw new InvalidOperationException("No image ID regex!"), 
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
    }

    protected Dictionary<string, object> GetDefaultData()
    {
        var adminid = Request.Cookies[AdminIdKey] ?? "";
        return new Dictionary<string, object>()
        {
            { "wwwrootversion", "1" },
            { "isAdmin", adminid == config.AdminId},
            { AdminIdKey, adminid },
            { PostStyleKey, Request.Cookies[PostStyleKey] ?? "" },
            { "requestUri", Request.GetDisplayUrl() }
        };
    }

    [HttpGet("robots.txt")]
    public string GetRobots() { return "User-agent: *\nDisallow: /\n"; }

    [HttpGet()]
    public async Task<ContentResult> GetIndexAsync()
    {
        //Need to look up threads? AND posts?? wow 
        var data = GetDefaultData();
        var threads = dbContext.Threads.Include(x => x.Posts).Where(x => !x.deleted);
        data["threads"] = await threads.Select(x => new ThreadView
        {
            tid = x.tid,
            subject = x.subject,
            created = x.created,
            postCount = x.Posts.Count(),
            lastPostOn = x.Posts.Max(x => (DateTime?)x.created) ?? new DateTime(0),
            link = $"/thread/{x.tid}",
        }).OrderByDescending(x => x.tid).ToListAsync();

        return new ContentResult{
            ContentType = "text/html",
            Content = await pageRenderer.RenderPageAsync("index", data)
        };
    }

    protected Task<List<PostView>> ConvertPosts(IQueryable<Post> query)
    {
        //  return substr(base64_encode(hash("sha512", $this->tripRaw, true)), 0, 10);
        using(var sha512 = SHA512.Create())
        {
            return query.Select(x => new PostView
            {
                tid = x.tid,
                pid = x.pid,
                content = x.content,
                createdOn = x.created,
                ipAddress = x.ipaddress,
                trip = string.IsNullOrEmpty(x.tripraw) ? "" : Convert.ToBase64String(
                    sha512.ComputeHash(System.Text.Encoding.UTF8.GetBytes(x.tripraw))).Substring(0, 10),
                realUsername = string.IsNullOrEmpty(x.username) ? "Anonymous" : x.username,
                link = $"/thread/{x.tid}#p{x.pid}",
                imageLink = $"/i/{x.image ?? "UNDEFINED"}",
                isBanned = false, //TODO: GET BANS
                hasImage = !string.IsNullOrEmpty(x.image)
            }).OrderByDescending(x => x.tid).ToListAsync();
        }
    }

    [HttpGet("thread/{id}")]
    public async Task<IActionResult> GetThreadAsync([FromRoute]int id)
    {
        var data = GetDefaultData();
        var thread = await dbContext.Threads.Where(x => x.tid == id).FirstOrDefaultAsync();

        if(thread == null || thread.deleted)
            return NotFound();

        var posts = dbContext.Posts.Include(x => x.Thread).Where(x => x.tid == id);

        data["thread"] = thread;
        data["posts"] = await ConvertPosts(posts);

        return new ContentResult{
            ContentType = "text/html",
            Content = await pageRenderer.RenderPageAsync("thread", data)
        };
    }

    [HttpGet("image")]
    public async Task<IActionResult> GetImageList([FromQuery]string bucket)
    {
        var data = GetDefaultData();
        data["bucket"] = bucket;

        //???
        data["publicLink"] = "???";
        data["readonly"] = false; //??
        data["ipp"] = 50; //image per page
        data["hideuploads"] = false; //???
        data["previousPage"] = 1;
        data["nextPage"] = 2;
        data["view"] = "???";

        //Are THESE the images to display???
        data["pastImages"] = "???";

        return new ContentResult{
            ContentType = "text/html",
            Content = await pageRenderer.RenderPageAsync("image", data)
        };
    }

    [HttpPost("submitpost")]
    public string SubmitPost()
    {
        return "kland is readonly for now, sorry";
    }

    public class SettingsForm
    {
        public string? adminid {get;set;}
        public string? poststyle {get;set;}
        public string? redirect {get;set;}
    }


    [HttpPost("settings")]
    public IActionResult PostSettings([FromForm]SettingsForm form)
    {
        var options = new Microsoft.AspNetCore.Http.CookieOptions()
        {
            Path = "/",
            Expires = DateTime.Now.AddHours(config.CookieExpireHours)
        };

        if(form.adminid == null) Response.Cookies.Delete(AdminIdKey);
        else Response.Cookies.Append(AdminIdKey, form.adminid, options);

        if(string.IsNullOrWhiteSpace(form.poststyle)) Response.Cookies.Delete(PostStyleKey);
        else Response.Cookies.Append(PostStyleKey, form.poststyle, options);

        return Redirect(form.redirect ?? "/");
    }

    [HttpPost("admin")]
    public string PostAdmin()
    {
        return "kland is readonly for now, sorry";
    }

    [HttpGet("i/{id}")]
    [ResponseCache(Duration = 13824000)] //six months
    public async Task<IActionResult> GetImageAsync([FromRoute] string id) //, [FromQuery] GetFileModify modify)
    {
        var match = idRegex.Match(id);

        if(!match.Success)
            return BadRequest($"Image ID not in specified format: {config.IdRegex}");

        //Go get it from s3
        try
        {
            var obj = await s3client.GetObjectAsync(config.Bucket, id);
            Response.Headers.Add("ETag", config.ETagPrepend + id);
            return File(obj.ResponseStream, "image/" + match.Groups[1].Value?.TrimStart('.')); 
        }
        catch(Exception ex)
        {
            _logger.LogError($"Exception during image request: {ex}");
            return NotFound();
        }
    }
}
