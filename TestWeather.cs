using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

// Simple test to get weather for Seattle
var httpClient = new HttpClient() { Timeout = TimeSpan.FromSeconds(30) };

try
{
    var result = await WeatherTools.GetWeatherByCityAsync(httpClient, "Seattle", CancellationToken.None);
    Console.WriteLine(result);
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
}
finally
{
    httpClient.Dispose();
}
