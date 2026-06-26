using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;

[McpServerToolType]
public static class WeatherTools
{
    [McpServerTool(Name = "getWeatherByCountry"), Description("Gets current weather (temp C, humidity %, wind m/s) for the specified country name.")]
    public static async Task<string> GetWeatherByCountryAsync(
    HttpClient httpClient,
    [Description("Country name, e.g. 'France', 'India', 'United States'")] string country,
    CancellationToken cancellationToken)
    => await GetWeatherAsync(httpClient, country, "country", cancellationToken);

    [McpServerTool(Name = "getWeatherByState"), Description("Gets current weather (temp C, humidity %, wind m/s) for the specified state / region name.")]
    public static async Task<string> GetWeatherByStateAsync(
    HttpClient httpClient,
    [Description("State or region name, e.g. 'California', 'Queensland', 'Bavaria'")] string state,
    CancellationToken cancellationToken)
    => await GetWeatherAsync(httpClient, state, "state", cancellationToken);

    [McpServerTool(Name = "getWeatherByCity"), Description("Gets current weather (temp C, humidity %, wind m/s) for the specified city name.")]
    public static async Task<string> GetWeatherByCityAsync(
    HttpClient httpClient,
    [Description("City name, e.g. 'Seattle', 'London', 'Tokyo'")] string city,
    CancellationToken cancellationToken)
    => await GetWeatherAsync(httpClient, city, "city", cancellationToken);

    private static async Task<string> GetWeatherAsync(HttpClient httpClient, string location, string v, CancellationToken cancellationToken)
    {
        if (httpClient == null) throw new ArgumentNullException(nameof(httpClient));
        if (string.IsNullOrWhiteSpace(location)) return "Location is required.";

        // Use Open-Meteo geocoding API to resolve the location to coordinates
        var geocodeUrl = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(location)}&count=1";
        GeocodeResponse geocode;
        try
        {
            geocode = await GetJsonOrThrowAsync<GeocodeResponse>(httpClient, geocodeUrl, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return $"Geocoding request failed: {ex.Message}";
        }

        var place = geocode?.results?.FirstOrDefault();
        if (place == null) return $"Location '{location}' not found.";

        var lat = place.latitude;
        var lon = place.longitude;
        var displayName = place.name;
        if (!string.IsNullOrEmpty(place.admin1)) displayName += $", {place.admin1}";
        if (!string.IsNullOrEmpty(place.country)) displayName += $", {place.country}";

        // Request current weather and hourly humidity from Open-Meteo
        var forecastUrl = $"https://api.open-meteo.com/v1/forecast?latitude={lat.ToString(CultureInfo.InvariantCulture)}&longitude={lon.ToString(CultureInfo.InvariantCulture)}&current_weather=true&hourly=relativehumidity_2m&temperature_unit=celsius&windspeed_unit=ms&timezone=UTC";
        ForecastResponse forecast;
        try
        {
            forecast = await GetJsonOrThrowAsync<ForecastResponse>(httpClient, forecastUrl, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return $"Weather request failed: {ex.Message}";
        }

        if (forecast == null || forecast.current_weather == null) return "Weather data unavailable.";

        var temp = forecast.current_weather.temperature;
        var wind = forecast.current_weather.windspeed;
        var timeUtc = forecast.current_weather.time; // ISO 8601 in UTC

        double? humidity = null;
        if (forecast.hourly?.time != null && forecast.hourly.relativehumidity_2m != null && forecast.hourly.time.Length > 0 && forecast.hourly.relativehumidity_2m.Length > 0)
        {
            var timesArr = forecast.hourly.time;
            var rh = forecast.hourly.relativehumidity_2m;

            // Try exact match (string equality)
            var idx = Array.FindIndex(timesArr, t => string.Equals(t, timeUtc, StringComparison.Ordinal));
            if (idx >= 0 && idx < rh.Length)
            {
                humidity = rh[idx];
            }
            else
            {
                // Parse timestamps and find nearest using DateTimeOffset with invariant culture and explicit styles
                try
                {
                    if (DateTimeOffset.TryParse(timeUtc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var target))
                    {
                        int nearestIndex = -1;
                        var minDiff = TimeSpan.MaxValue;
                        for (int i = 0; i < timesArr.Length; i++)
                        {
                            if (!DateTimeOffset.TryParse(timesArr[i], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var ts))
                                continue;

                            var diff = (ts - target).Duration();
                            if (diff < minDiff)
                            {
                                minDiff = diff;
                                nearestIndex = i;
                            }
                        }

                        if (nearestIndex >= 0 && nearestIndex < rh.Length)
                            humidity = rh[nearestIndex];
                    }
                }
                catch
                {
                    // ignore parsing errors, humidity will remain null
                }
            }
        }

        var humStr = humidity.HasValue ? $"{Math.Round(humidity.Value)}%" : "N/A";
        return $"Current weather for {displayName}: {Math.Round(temp, 1)}°C, Humidity {humStr}, Wind {Math.Round(wind, 1)} m/s";
    }

    private static async Task<T> GetJsonOrThrowAsync<T>(HttpClient httpClient, string url, CancellationToken cancellationToken)
    {
        if (httpClient == null) throw new ArgumentNullException(nameof(httpClient));
        using var resp = await httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var payload = await resp.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken).ConfigureAwait(false);
        return payload;
    }

    private class GeocodeResponse
    {
        public GeocodeResult[] results { get; set; }
    }

    private class GeocodeResult
    {
        public string name { get; set; }
        public double latitude { get; set; }
        public double longitude { get; set; }
        public string country { get; set; }
        public string admin1 { get; set; }
    }

    private class ForecastResponse
    {
        public CurrentWeather current_weather { get; set; }
        public Hourly hourly { get; set; }
    }

    private class CurrentWeather
    {
        public double temperature { get; set; }
        public double windspeed { get; set; }
        public string time { get; set; }
    }

    private class Hourly
    {
        public string[] time { get; set; }
        [JsonPropertyName("relativehumidity_2m")]
        public double[] relativehumidity_2m { get; set; }
    }
}
