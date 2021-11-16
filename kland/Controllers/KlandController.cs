using kland.Db;
using kland.Interfaces;
using kland.Views;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace kland.Controllers;

public class KlandControllerConfig
{
    public string AdminId {get;set;} = "PLEASECHANGE";
    public double CookieExpireHours {get;set;}
}

[ApiController]
[Route("")] //Default route goes here?
public class KlandController : KlandBase
{
    protected KlandControllerConfig config;
    protected IPageRenderer pageRenderer;

    public KlandController(ILogger<KlandController> logger, KlandDbContext dbContext,
        KlandControllerConfig config, IPageRenderer pageRenderer) : base(logger, dbContext)
    {
        this.config = config;
        this.pageRenderer = pageRenderer;
    }

    protected Dictionary<string, object> GetDefaultData()
    {
        var adminid = Request.Cookies[AdminIdKey] ?? "";
        return new Dictionary<string, object>()
        {
            { "wwwrootversion", GetType().Assembly.GetName().Version?.ToString() ?? "UNKNOWN" },
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

    public class GetImageQuery
    {
        public string? bucket {get;set;}
        public bool asJSON {get;set;}
        public int page {get;set;}
        public int? ipp {get;set;}
        public string? view {get;set;}
    }

    [HttpGet("image")]
    public async Task<IActionResult> GetImageList([FromQuery]GetImageQuery query)
    {
        var data = GetDefaultData();

        var page = query.page;
        var view = query.view ?? "";
        var bucket = query.bucket ?? "";
        var ipp = query.ipp ?? 20;
        int.TryParse(Request.Cookies["ipp"], out ipp);
        if(page < 1) page = 1;
        if(ipp <= 0) ipp = 20;

        data["bucket"] = bucket;
        data["ipp"] = ipp;
        data["view"] = view;
        data["page"] = page;
        data["nextPage"] = page + 1;
        if(page > 1) data["previousPage"] = page - 1;
        data["hideuploads"] = false; //An OLD setting that we used when abuse was happening

        kland.Db.Thread? thread = null;

        //Hunt for threads based on the hash (which is view)
        if(!string.IsNullOrEmpty(view))
        {
            thread = await dbContext.Threads.Where(x => x.hash == view && x.subject.StartsWith(OrphanedPrepend)).FirstOrDefaultAsync();
            data["readonly"] = true;
        }
        else //This is either normal bucket or... whatever, default bucket
        {
            var threadName = OrphanedPrepend;
            if(!string.IsNullOrEmpty(bucket))
                threadName += "_" + bucket;
            thread = await dbContext.Threads.Where(x => x.subject == threadName).FirstOrDefaultAsync();
        }

        if(thread != null)
        {
            data["publicLink"] = $"/image?view={thread.hash}";
            var postQuery = dbContext.Posts.Where(x => x.tid == thread.tid).OrderByDescending(x => x.pid).Skip((page - 1) * ipp).Take(ipp);
            data["pastImages"] = await ConvertPosts(postQuery);
        }

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
}
