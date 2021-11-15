using System.ComponentModel.DataAnnotations;

namespace kland.Db
{
    public class Ban
    {
        [Key]
        public string range {get;set;} = "";
        public DateTime created {get;set;}
        public string? note {get;set;}
    }
}