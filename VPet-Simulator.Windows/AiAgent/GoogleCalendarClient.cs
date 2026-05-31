using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VPet_Simulator.Windows.AiAgent;

internal sealed class GoogleCalendarClient
{
    private const string CalendarScope = "https://www.googleapis.com/auth/calendar.readonly";
    private const string RedirectUri = "http://127.0.0.1:53682/";
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(60)
    };

    private string accessToken = "";
    private DateTime accessTokenExpiresAt = DateTime.MinValue;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(AiAgentEnvironment.Get(AiAgentEnvironment.GoogleRefreshToken));

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        var clientId = AiAgentEnvironment.Get(AiAgentEnvironment.GoogleClientId);
        var clientSecret = AiAgentEnvironment.Get(AiAgentEnvironment.GoogleClientSecret);
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            throw new InvalidOperationException("\u8acb\u5148\u8a2d\u5b9a Google OAuth Client ID \u548c Client Secret\u3002");

        using var listener = new HttpListener();
        listener.Prefixes.Add(RedirectUri);
        listener.Start();

        var authUrl =
            "https://accounts.google.com/o/oauth2/v2/auth" +
            "?response_type=code" +
            "&access_type=offline" +
            "&prompt=consent" +
            "&client_id=" + Uri.EscapeDataString(clientId) +
            "&redirect_uri=" + Uri.EscapeDataString(RedirectUri) +
            "&scope=" + Uri.EscapeDataString(CalendarScope);

        Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });

        var contextTask = listener.GetContextAsync();
        using var registration = cancellationToken.Register(() => listener.Close());
        var context = await contextTask;
        var code = context.Request.QueryString["code"];

        var html = Encoding.UTF8.GetBytes("<html><body>Google \u884c\u4e8b\u66c6\u5df2\u9023\u7dda\uff0c\u53ef\u4ee5\u95dc\u9589\u9019\u500b\u5206\u9801\u3002</body></html>");
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength64 = html.Length;
        await context.Response.OutputStream.WriteAsync(html, cancellationToken);
        context.Response.Close();

        if (string.IsNullOrWhiteSpace(code))
            throw new InvalidOperationException("Google \u6c92\u6709\u56de\u50b3\u6388\u6b0a\u78bc\u3002");

        var token = await RequestTokenAsync(new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["redirect_uri"] = RedirectUri,
            ["grant_type"] = "authorization_code"
        }, cancellationToken);

        if (token.RefreshToken == "")
            throw new InvalidOperationException("Google \u6c92\u6709\u56de\u50b3 refresh token\u3002\u8acb\u6aa2\u67e5 OAuth \u540c\u610f\u756b\u9762\u8a2d\u5b9a\u5f8c\u518d\u8a66\u4e00\u6b21\u3002");

        AiAgentEnvironment.SetUser(AiAgentEnvironment.GoogleRefreshToken, token.RefreshToken);
        accessToken = token.AccessToken;
        accessTokenExpiresAt = DateTime.Now.AddSeconds(token.ExpiresIn - 60);
    }

    public async Task<IReadOnlyList<CalendarEventInfo>> GetUpcomingEventsAsync(TimeSpan window, CancellationToken cancellationToken)
    {
        var token = await GetAccessTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
            return [];

        var timeMin = Uri.EscapeDataString(DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
        var timeMax = Uri.EscapeDataString(DateTime.UtcNow.Add(window).ToString("o", CultureInfo.InvariantCulture));
        var url = $"https://www.googleapis.com/calendar/v3/calendars/primary/events?singleEvents=true&orderBy=startTime&maxResults=20&timeMin={timeMin}&timeMax={timeMax}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Google \u884c\u4e8b\u66c6\u8b80\u53d6\u5931\u6557\uff1a{(int)response.StatusCode} {response.ReasonPhrase}");

        using var document = JsonDocument.Parse(responseText);
        if (!document.RootElement.TryGetProperty("items", out var items))
            return [];

        return items.EnumerateArray()
            .Select(ParseEvent)
            .Where(x => x != null)
            .Cast<CalendarEventInfo>()
            .OrderBy(x => x.Start)
            .ToList();
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(accessToken) && accessTokenExpiresAt > DateTime.Now)
            return accessToken;

        var refreshToken = AiAgentEnvironment.Get(AiAgentEnvironment.GoogleRefreshToken);
        if (string.IsNullOrWhiteSpace(refreshToken))
            return "";

        var clientId = AiAgentEnvironment.Get(AiAgentEnvironment.GoogleClientId);
        var clientSecret = AiAgentEnvironment.Get(AiAgentEnvironment.GoogleClientSecret);
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            return "";

        var token = await RequestTokenAsync(new Dictionary<string, string>
        {
            ["refresh_token"] = refreshToken,
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["grant_type"] = "refresh_token"
        }, cancellationToken);

        accessToken = token.AccessToken;
        accessTokenExpiresAt = DateTime.Now.AddSeconds(token.ExpiresIn - 60);
        return accessToken;
    }

    private static async Task<TokenResponse> RequestTokenAsync(Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await HttpClient.PostAsync("https://oauth2.googleapis.com/token", content, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Google OAuth \u5931\u6557\uff1a{(int)response.StatusCode} {response.ReasonPhrase}");

        using var document = JsonDocument.Parse(responseText);
        return new TokenResponse
        {
            AccessToken = document.RootElement.GetProperty("access_token").GetString() ?? "",
            RefreshToken = document.RootElement.TryGetProperty("refresh_token", out var refresh) ? refresh.GetString() ?? "" : "",
            ExpiresIn = document.RootElement.TryGetProperty("expires_in", out var expires) ? expires.GetInt32() : 3600
        };
    }

    private static CalendarEventInfo? ParseEvent(JsonElement item)
    {
        if (!item.TryGetProperty("start", out var startElement))
            return null;

        var isAllDay = false;
        DateTime start;
        if (startElement.TryGetProperty("dateTime", out var startDateTime))
        {
            start = DateTime.Parse(startDateTime.GetString() ?? "", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToLocalTime();
        }
        else if (startElement.TryGetProperty("date", out var startDate))
        {
            isAllDay = true;
            start = DateTime.Parse(startDate.GetString() ?? "", CultureInfo.InvariantCulture);
        }
        else
        {
            return null;
        }

        var end = start;
        if (item.TryGetProperty("end", out var endElement))
        {
            if (endElement.TryGetProperty("dateTime", out var endDateTime))
                end = DateTime.Parse(endDateTime.GetString() ?? "", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToLocalTime();
            else if (endElement.TryGetProperty("date", out var endDate))
                end = DateTime.Parse(endDate.GetString() ?? "", CultureInfo.InvariantCulture);
        }

        return new CalendarEventInfo
        {
            Id = item.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
            Summary = item.TryGetProperty("summary", out var summary) ? summary.GetString() ?? "(\u7121\u6a19\u984c)" : "(\u7121\u6a19\u984c)",
            Start = start,
            End = end,
            IsAllDay = isAllDay
        };
    }

    private sealed class TokenResponse
    {
        public string AccessToken { get; init; } = "";
        public string RefreshToken { get; init; } = "";
        public int ExpiresIn { get; init; }
    }
}
