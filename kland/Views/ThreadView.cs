namespace kland.Views;

public class ThreadView
{
    public int tid {get;set;}
    public string link {get;set;} = "";
    public string subject {get;set;} = "";
    public DateTime lastPostOn {get;set;}
    public DateTime created {get;set;}
    public int postCount {get;set;}
}