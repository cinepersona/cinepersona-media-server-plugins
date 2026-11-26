#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CinePersona.MediaSync
{
    public sealed class MediaLibraryEntryInput
    {
        public string? Imdb { get; set; }

        public int Tmdb { get; set; }

        public int Width { get; set; }

        public int Height { get; set; }
    }

    public sealed class MediaLibrarySyncResult
    {
        public bool Pushed { get; set; }

        public bool Skipped { get; set; }

        public string Fingerprint { get; set; } = string.Empty;

        public int ItemCount { get; set; }
    }

    // Shared by both Jellyfin and Emby plugins: pushes only public TMDB/IMDb IDs
    // and coarse resolution tags (4K/1080p/720p) to CinePersona without exposing
    // any media server URLs, IPs, or credentials.
    public sealed class MediaLibrarySync
    {
        private const int MaxItems = 20000;
        private static readonly TimeSpan RefreshHeartbeat = TimeSpan.FromHours(24);
        private static readonly Regex ImdbRegex = new Regex(@"^tt\d{5,10}$", RegexOptions.Compiled);
        private static readonly Regex ServerKeyRegex = new Regex(@"[^A-Za-z0-9_-]+", RegexOptions.Compiled);

        private readonly HttpClient client;
        private readonly Func<object, string> serialize;
        private readonly Func<DateTime> clock;

        public MediaLibrarySync(HttpClient client, Func<object, string> serialize, Func<DateTime>? clock = null)
        {
            this.client = client;
            this.serialize = serialize;
            this.clock = clock ?? (() => DateTime.UtcNow);
        }

        public static string ClassifyQuality(int width, int height)
        {
            if (width >= 3600 || height >= 2000) return "4K";
            if (width >= 1800 || height >= 1000) return "1080p";
            if (width >= 1200 || height >= 680) return "720p";
            return string.Empty;
        }

        public static string NormalizeServerKey(string? raw, string fallback)
        {
            var cleaned = ServerKeyRegex.Replace((raw ?? string.Empty).Trim(), "-").Trim('-');
            if (cleaned.Length == 0) cleaned = fallback;
            if (cleaned.Length > 64) cleaned = cleaned.Substring(0, 64);
            return cleaned;
        }

        public async Task<MediaLibrarySyncResult> PushAsync(
            string serverUrl,
            string apiKey,
            string source,
            string serverKey,
            string clientName,
            IEnumerable<MediaLibraryEntryInput> rawItems,
            string? lastHash,
            DateTime? lastSyncedAt,
            bool force = false)
        {
            var trimmedKey = (apiKey ?? string.Empty).Trim();
            var normalizedSource = (source ?? string.Empty).Trim().ToLowerInvariant();
            if (trimmedKey.Length == 0
                || (normalizedSource != "jellyfin" && normalizedSource != "emby" && normalizedSource != "plex"))
            {
                return new MediaLibrarySyncResult { Skipped = true };
            }

            var baseUrl = string.IsNullOrWhiteSpace(serverUrl)
                ? "https://cinepersona.com"
                : serverUrl.Trim().TrimEnd('/');
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var serverUri)
                || serverUri == null
                || (serverUri.Scheme != Uri.UriSchemeHttp && serverUri.Scheme != Uri.UriSchemeHttps))
            {
                return new MediaLibrarySyncResult { Skipped = true };
            }

            var normalizedItems = NormalizeItems(rawItems);
            if (normalizedItems.Count == 0 && !lastSyncedAt.HasValue)
            {
                return new MediaLibrarySyncResult { Skipped = true, ItemCount = 0 };
            }

            var fingerprint = ComputeFingerprint(normalizedItems);
            var now = clock().ToUniversalTime();
            if (!force
                && !string.IsNullOrEmpty(lastHash)
                && string.Equals(lastHash, fingerprint, StringComparison.Ordinal)
                && lastSyncedAt.HasValue
                && now - lastSyncedAt.Value.ToUniversalTime() < RefreshHeartbeat)
            {
                return new MediaLibrarySyncResult
                {
                    Skipped = true,
                    Fingerprint = fingerprint,
                    ItemCount = normalizedItems.Count
                };
            }

            var payloadItems = new List<Dictionary<string, object>>(normalizedItems.Count);
            foreach (var item in normalizedItems)
            {
                var entry = new Dictionary<string, object>
                {
                    ["imdb"] = item.Imdb,
                    ["tmdb"] = item.Tmdb,
                    ["quality"] = item.Quality
                };
                payloadItems.Add(entry);
            }

            var safeServer = NormalizeServerKey(serverKey, normalizedSource + "-plugin");
            var safeClient = string.IsNullOrWhiteSpace(clientName) ? "plugin" : clientName.Trim();
            if (safeClient.Length > 40) safeClient = safeClient.Substring(0, 40);

            var payload = new Dictionary<string, object>
            {
                ["server"] = safeServer,
                ["client"] = safeClient,
                ["items"] = payloadItems
            };

            var json = serialize(payload);
            var endpoint = new Uri(serverUri, "/open/v1/media-library/" + normalizedSource);
            for (var attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    using (var request = new HttpRequestMessage(HttpMethod.Put, endpoint))
                    using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20)))
                    {
                        request.Headers.Add("X-API-Key", trimmedKey);
                        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                        using (var response = await client.SendAsync(request, timeout.Token).ConfigureAwait(false))
                        {
                            if (response.IsSuccessStatusCode)
                            {
                                return new MediaLibrarySyncResult
                                {
                                    Pushed = true,
                                    Fingerprint = fingerprint,
                                    ItemCount = normalizedItems.Count
                                };
                            }
                            if (attempt != 0 || ((int)response.StatusCode != 429 && (int)response.StatusCode < 500))
                            {
                                throw new InvalidOperationException("CinePersona media library sync returned HTTP " + (int)response.StatusCode);
                            }
                        }
                    }
                }
                catch (HttpRequestException) when (attempt == 0) { }
                catch (OperationCanceledException) when (attempt == 0) { }
                await Task.Delay(1000).ConfigureAwait(false);
            }

            return new MediaLibrarySyncResult { Skipped = true, Fingerprint = fingerprint, ItemCount = normalizedItems.Count };
        }

        private static List<NormalizedItem> NormalizeItems(IEnumerable<MediaLibraryEntryInput>? rawItems)
        {
            var deduped = new Dictionary<string, NormalizedItem>(StringComparer.Ordinal);
            if (rawItems == null)
            {
                return new List<NormalizedItem>();
            }

            foreach (var raw in rawItems)
            {
                if (raw == null) continue;
                var imdb = (raw.Imdb ?? string.Empty).Trim().ToLowerInvariant();
                if (!ImdbRegex.IsMatch(imdb)) imdb = string.Empty;
                var tmdb = raw.Tmdb > 0 ? raw.Tmdb : 0;
                if (imdb.Length == 0 && tmdb == 0) continue;

                var quality = ClassifyQuality(raw.Width, raw.Height);
                var key = tmdb > 0
                    ? "t:" + tmdb.ToString(CultureInfo.InvariantCulture)
                    : "i:" + imdb;

                if (deduped.TryGetValue(key, out var existing))
                {
                    if (existing.Imdb.Length == 0 && imdb.Length > 0) existing.Imdb = imdb;
                    existing.Quality = BetterQuality(existing.Quality, quality);
                }
                else if (deduped.Count < MaxItems)
                {
                    deduped[key] = new NormalizedItem
                    {
                        Imdb = imdb,
                        Tmdb = tmdb,
                        Quality = quality
                    };
                }
            }

            var list = deduped.Values.ToList();
            list.Sort((a, b) =>
            {
                var cmp = a.Tmdb.CompareTo(b.Tmdb);
                if (cmp != 0) return cmp;
                return string.CompareOrdinal(a.Imdb, b.Imdb);
            });
            return list;
        }

        private static string BetterQuality(string a, string b)
        {
            return QualityRank(b) > QualityRank(a) ? b : a;
        }

        private static int QualityRank(string q)
        {
            if (q == "4K") return 3;
            if (q == "1080p") return 2;
            if (q == "720p") return 1;
            return 0;
        }

        private static string ComputeFingerprint(List<NormalizedItem> items)
        {
            var sb = new StringBuilder(items.Count * 18);
            foreach (var item in items)
            {
                sb.Append(item.Tmdb.ToString(CultureInfo.InvariantCulture))
                  .Append('|')
                  .Append(item.Imdb)
                  .Append('|')
                  .Append(item.Quality)
                  .Append('\n');
            }
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
                var hex = new StringBuilder(hash.Length * 2);
                foreach (var b in hash)
                {
                    hex.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                }
                return hex.ToString();
            }
        }

        private sealed class NormalizedItem
        {
            public string Imdb = string.Empty;
            public int Tmdb;
            public string Quality = string.Empty;
        }
    }
}
