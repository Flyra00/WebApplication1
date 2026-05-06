using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using WebApplication1.Models;

namespace WebApplication1.Services.Midtrans
{
    public class MidtransService
    {
        private readonly HttpClient _httpClient;
        private readonly MidtransOptions _options;

        public MidtransService(HttpClient httpClient, IOptions<MidtransOptions> options)
        {
            _httpClient = httpClient;
            _options = options.Value;
        }

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(_options.ServerKey) &&
            !string.IsNullOrWhiteSpace(_options.ClientKey);

        public string ClientKey => _options.ClientKey;

        public bool IsProduction => _options.IsProduction;

        public async Task<MidtransCreateTransactionResult> CreateSnapTransactionAsync(
            string orderId,
            decimal grossAmount,
            string firstName,
            string? email,
            string? phone,
            CancellationToken cancellationToken = default)
        {
            var payload = new
            {
                transaction_details = new
                {
                    order_id = orderId,
                    gross_amount = (int)Math.Ceiling(grossAmount)
                },
                credit_card = new
                {
                    secure = true
                },
                customer_details = new
                {
                    first_name = string.IsNullOrWhiteSpace(firstName) ? "Guest" : firstName,
                    email,
                    phone
                }
            };

            var request = new HttpRequestMessage(HttpMethod.Post, GetSnapTransactionEndpoint());
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", GetEncodedServerKey());
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new MidtransCreateTransactionResult
                {
                    Success = false,
                    ErrorMessage = responseBody
                };
            }

            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;
            var token = root.TryGetProperty("token", out var tokenElement) ? tokenElement.GetString() : null;
            var redirectUrl = root.TryGetProperty("redirect_url", out var redirectElement) ? redirectElement.GetString() : null;

            if (string.IsNullOrWhiteSpace(token))
            {
                return new MidtransCreateTransactionResult
                {
                    Success = false,
                    ErrorMessage = "Token transaksi Midtrans tidak tersedia."
                };
            }

            return new MidtransCreateTransactionResult
            {
                Success = true,
                Token = token,
                RedirectUrl = redirectUrl
            };
        }

        public async Task<JsonDocument?> GetTransactionStatusAsync(string orderId, CancellationToken cancellationToken = default)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, GetStatusEndpoint(orderId));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", GetEncodedServerKey());

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            return JsonDocument.Parse(responseBody);
        }

        public bool VerifySignature(string orderId, string statusCode, string grossAmount, string signatureKey)
        {
            if (string.IsNullOrWhiteSpace(signatureKey) || string.IsNullOrWhiteSpace(_options.ServerKey))
                return false;

            var signaturePayload = string.Concat(orderId, statusCode, grossAmount, _options.ServerKey);
            var bytes = SHA512.HashData(Encoding.UTF8.GetBytes(signaturePayload));
            var expected = Convert.ToHexString(bytes).ToLowerInvariant();
            return string.Equals(expected, signatureKey, StringComparison.OrdinalIgnoreCase);
        }

        private string GetSnapTransactionEndpoint()
        {
            return _options.IsProduction
                ? "https://app.midtrans.com/snap/v1/transactions"
                : "https://app.sandbox.midtrans.com/snap/v1/transactions";
        }

        private string GetStatusEndpoint(string orderId)
        {
            var encodedOrderId = Uri.EscapeDataString(orderId);
            return _options.IsProduction
                ? $"https://api.midtrans.com/v2/{encodedOrderId}/status"
                : $"https://api.sandbox.midtrans.com/v2/{encodedOrderId}/status";
        }

        private string GetEncodedServerKey()
        {
            var raw = $"{_options.ServerKey}:";
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
        }
    }

    public class MidtransCreateTransactionResult
    {
        public bool Success { get; set; }

        public string? Token { get; set; }

        public string? RedirectUrl { get; set; }

        public string? ErrorMessage { get; set; }
    }
}
