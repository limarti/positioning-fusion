namespace Backend.Services;

public class ImuParser
{
    private readonly ILogger<ImuParser> _logger;

    public ImuParser(ILogger<ImuParser> logger)
    {
        _logger = logger;
    }
}