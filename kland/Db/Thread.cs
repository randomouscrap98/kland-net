using System.ComponentModel.DataAnnotations;

namespace kland.Db;

public class Thread
{
    [Key]
    public int tid { get; set; }

    public DateTime created { get; set; }
    public string subject { get; set; } = "";
    public bool deleted { get; set; }
    public string? hash { get; set; }

    public List<Post> Posts { get; set; } = new List<Post>(); //Will this break things?
}
