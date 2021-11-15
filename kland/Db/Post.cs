using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace kland.Db;

public class Post
{
    [Key]
    public int pid { get; set; }

    public DateTime created { get; set; }
    public string content { get; set; } = "";
    public string options { get; set; } = "";
    public string ipaddress { get; set; } = "";
    public string? username { get; set; }
    public string? tripraw { get; set; }
    public string? image { get; set; }

    public int tid { get; set; }

    [ForeignKey("tid")] //Only needed on the linked parent thing, not the id
    public Thread? Thread { get; set; }
}
