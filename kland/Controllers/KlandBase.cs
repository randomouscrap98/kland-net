using System.Security.Cryptography;
using System.Text;
using kland.Db;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace kland;

public class KlandBase : ControllerBase
{
    public const string AdminIdKey = "adminId";
    public const string PostStyleKey = "postStyle";
    public const string OrphanedPrepend = "Internal_OrphanedImages";

    protected readonly ILogger logger;
    protected KlandDbContext dbContext;

    protected Random random;


    public KlandBase(ILogger logger, KlandDbContext dbContext)
    {
        this.logger = logger;
        this.dbContext = dbContext;
        this.random = new Random();
    }

    protected string GetRandomAlphaString(int count)
    {
        var builder = new StringBuilder();

        for(var i = 0; i < count; i++)
            builder.Append((char)('a' + (random.Next() % 26)));

        return builder.ToString();
    }

    protected bool StringToBool(string value)
    {
        value = value.Trim().ToLower();
        return value != null && value != "0" && value != "false" && value != "undefined" && value != "null";
    }

    protected Task<List<PostView>> ConvertPosts(IQueryable<Post> query)
    {
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
}