namespace kland;

public class PostView
{
    public int pid {get;set;}
    public int tid {get;set;}
    public DateTime createdOn {get;set;}
    public string content {get;set;} = "";
    public string? realUsername {get;set;}
    public string? trip {get;set;}
    public bool hasImage {get;set;}
    public bool isBanned {get;set;}
    public string? imageLink {get;set;}
    public string? link {get;set;}
    public string ipAddress {get;set;} = "";
}