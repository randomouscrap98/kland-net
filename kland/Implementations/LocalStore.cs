namespace kland;

public class LocalStoreConfig
{
    public string? Folder {get;set;}
    public int MaxHashRetries {get;set;}
    public int MaxHashWait {get;set;}
}

public class LocalStore : IUploadStore
{
    protected ILogger logger;
    protected LocalStoreConfig config;

    protected readonly object HashLock = new object();

    public LocalStore(ILogger<LocalStore> logger, LocalStoreConfig config)
    {
        this.logger = logger;
        this.config = config;
    }

    public Task<byte[]> GetDataAsync(string id)
    {
        var path = Path.Join(config.Folder, id);

        if(!File.Exists(path))
            throw new InvalidOperationException("File not found");

        return File.ReadAllBytesAsync(path);
    }

    public async Task<string> PutDataAsync(byte[] data, Func<string> nameGenerator, string mimeType)
    {
        if(Monitor.TryEnter(HashLock, config.MaxHashWait))
        {
            try
            {
                string name = "";
                string path = "";

                for (int i = 0; i < config.MaxHashRetries; i++)
                {
                    name = nameGenerator();
                    path = Path.Join(config.Folder, name);

                    if(File.Exists(path))
                    {
                        //Getting here, it's a collision
                        logger.LogInformation($"Tried to generate existing file name {path}");

                        if (i >= config.MaxHashRetries - 1)
                            throw new InvalidOperationException($"Ran out of retries to generate hash! ({config.MaxHashRetries})");
                    }
                    else
                    {
                        break;
                    }
                }

                await File.WriteAllBytesAsync(path, data);

                return name;
            }
            finally
            {
                Monitor.Exit(HashLock);
            }
        }
        else
        {
            throw new InvalidOperationException($"Couldn't acquire local name hash lock! Max wait: {config.MaxHashWait}");
        }
    }
}