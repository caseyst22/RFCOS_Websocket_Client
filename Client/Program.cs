using Client;

class Program
{

    static async Task Main(string[] args)
    {
        TagBlinkReader reader = new TagBlinkReader();
        await reader.ReadRFServAsync();
    }

}