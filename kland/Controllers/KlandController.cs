using System.Text.RegularExpressions;
using Amazon.S3;
using kland.Db;
using Microsoft.AspNetCore.Mvc;

namespace kland.Controllers;

public class KlandControllerConfig
{
    public string? Bucket {get;set;}
    public string? IdRegex {get;set;}
    public string? ETagPrepend {get;set;}
}

[ApiController]
[Route("")] //Default route goes here?
public class KlandController : ControllerBase
{
    private readonly ILogger _logger;
    protected KlandDbContext dbContext;
    protected IAmazonS3 s3client;
    protected KlandControllerConfig config;
    protected Regex idRegex;

    public KlandController(ILogger<KlandController> logger, KlandDbContext dbContext, IAmazonS3 s3client,
        KlandControllerConfig config)
    {
        _logger = logger;
        this.dbContext = dbContext;
        this.s3client = s3client;
        this.config = config;

        //Just don't even bother if the config has no bucket. We want to immediately know when this is broken,
        //so it's ok to break the entire kland for this!
        if(string.IsNullOrWhiteSpace(config.Bucket))
            throw new InvalidOperationException("No bucket set for images!");

        idRegex = new Regex(config.IdRegex ?? throw new InvalidOperationException("No image ID regex!"), 
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
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
            //System.IO.File.OpenRead(finalPath), fileData.fileType);
        }
        catch(Exception ex)
        {
            _logger.LogError($"Exception during image request: {ex}");
            return NotFound();
        }
    }
}
