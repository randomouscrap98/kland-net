namespace kland.Interfaces;

public interface IPageRenderer
{
    Task<string> RenderPageAsync(string page, Dictionary<string, string> data);
}