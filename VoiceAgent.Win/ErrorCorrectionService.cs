using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace VoiceAgent.Win
{
    public class CorrectionResponse
    {
        [JsonPropertyName("status")] public string Status { get; set; } = string.Empty;
        [JsonPropertyName("type")] public string Type { get; set; } = string.Empty;
        [JsonPropertyName("result_text")] public string CorrectedText { get; set; } = string.Empty;
        [JsonPropertyName("command_type")] public int CommandType { get; set; }
    }

    public class ErrorCorrectionService
    {
        private readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        public async Task<CorrectionResponse?> CorrectTextAsync(string inputText, string command)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync("http://140.115.54.55:1228/error_correction/", new { text = inputText, command = command });
                if (response.IsSuccessStatusCode)
                {
                    return JsonSerializer.Deserialize<CorrectionResponse>(await response.Content.ReadAsStringAsync(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
                return null;
            }
            catch { return null; }
        }
    }
}