using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

static class Program
{
    private static readonly HttpClient Http = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(12)
    };

    private static readonly ExchangeRateService Rates = new ExchangeRateService(Http);

    static async Task Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("KztPriceConsole", "1.0"));

        while (true)
        {
            Console.WriteLine();
            Console.WriteLine("=== KZT Price Console (C#) ===");
            Console.WriteLine("1) Конвертер валют -> KZT (по текущему курсу онлайн)");
            Console.WriteLine("2) Мини-магазин (цены в USD/EUR/RUB -> вывод в KZT)");
            Console.WriteLine("0) Выход");
            Console.Write("Выбор: ");

            var choice = Console.ReadLine()?.Trim();

            if (choice == "0")
                return;

            if (choice == "1")
                await RunConverter();

            else if (choice == "2")
                await RunMiniShop();

            else
                Console.WriteLine("❌ Неверный выбор.");
        }
    }

    private static async Task RunConverter()
    {
        Console.WriteLine();
        Console.WriteLine("--- Конвертер -> KZT ---");

        decimal amount = ReadDecimal("Введите сумму (например 19.99): ");
        string from = ReadCurrencyCode("Введите валюту (USD/EUR/RUB/KZT ...): ");

        var result = await Rates.ConvertToKztAsync(amount, from);

        if (!result.Success)
        {
            Console.WriteLine($"❌ Ошибка: {result.ErrorMessage}");
            return;
        }

        Console.WriteLine();
        Console.WriteLine($"Курс: 1 {from} = {result.RateToKzt:F4} KZT");
        Console.WriteLine($"Обновлено: {result.LastUpdateUtc}");
        Console.WriteLine($"Итого: {amount.ToString("F2", CultureInfo.InvariantCulture)} {from} = {result.AmountKzt:F2} KZT");
    }

    private static async Task RunMiniShop()
    {
        Console.WriteLine();
        Console.WriteLine("--- Мини-магазин ---");

        var products = new List<Product>
        {
            new("USB-C Cable", 4.99m, "USD"),
            new("Wireless Mouse", 12.50m, "USD"),
            new("Mechanical Keyboard", 39.00m, "EUR"),
            new("Headphones", 3500m, "RUB"),
            new("Monitor 24\"", 120m, "USD"),
        };

        while (true)
        {
            Console.WriteLine();
            Console.WriteLine("Товары:");
            for (int i = 0; i < products.Count; i++)
            {
                var p = products[i];
                Console.WriteLine($"{i + 1}) {p.Name} — {p.Price.ToString("F2", CultureInfo.InvariantCulture)} {p.Currency}");
            }
            Console.WriteLine("0) Назад");
            Console.Write("Выбор товара: ");

            var input = Console.ReadLine()?.Trim();
            if (input == "0") return;

            if (!int.TryParse(input, out int index) || index < 1 || index > products.Count)
            {
                Console.WriteLine("❌ Неверный номер товара.");
                continue;
            }

            var product = products[index - 1];
            int qty = ReadInt("Количество: ", min: 1);

            decimal total = product.Price * qty;

            var result = await Rates.ConvertToKztAsync(total, product.Currency);
            if (!result.Success)
            {
                Console.WriteLine($"❌ Ошибка: {result.ErrorMessage}");
                continue;
            }

            Console.WriteLine();
            Console.WriteLine($"Товар: {product.Name}");
            Console.WriteLine($"Сумма: {total.ToString("F2", CultureInfo.InvariantCulture)} {product.Currency} (x{qty})");
            Console.WriteLine($"Курс: 1 {product.Currency} = {result.RateToKzt:F4} KZT");
            Console.WriteLine($"Обновлено: {result.LastUpdateUtc}");
            Console.WriteLine($"К оплате: {result.AmountKzt:F2} KZT");
        }
    }

    private static decimal ReadDecimal(string prompt)
    {
        while (true)
        {
            Console.Write(prompt);
            var s = (Console.ReadLine() ?? "").Trim().Replace(',', '.');

            if (decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var val) && val > 0)
                return val;

            Console.WriteLine("❌ Введите число > 0 (пример: 19.99).");
        }
    }

    private static int ReadInt(string prompt, int min)
    {
        while (true)
        {
            Console.Write(prompt);
            var s = (Console.ReadLine() ?? "").Trim();

            if (int.TryParse(s, out var val) && val >= min)
                return val;

            Console.WriteLine($"❌ Введите целое число >= {min}.");
        }
    }

    private static string ReadCurrencyCode(string prompt)
    {
        while (true)
        {
            Console.Write(prompt);
            var code = (Console.ReadLine() ?? "").Trim().ToUpperInvariant();

            if (code.Length == 3)
                return code;

            Console.WriteLine("❌ Валюта должна быть из 3 букв (USD/EUR/RUB/KZT...).");
        }
    }
}

public sealed record Product(string Name, decimal Price, string Currency);

public sealed class ExchangeRateService
{
    private readonly HttpClient _http;

    private readonly Dictionary<string, (DateTimeOffset fetchedAt, ExchangeRateApiResponse data)> _cache = new();
    private readonly object _lock = new();

    public ExchangeRateService(HttpClient http) => _http = http;

    public async Task<ConvertResult> ConvertToKztAsync(decimal amount, string fromCurrency)
    {
        if (amount <= 0)
            return ConvertResult.Fail("Сумма должна быть > 0.");

        if (string.Equals(fromCurrency, "KZT", StringComparison.OrdinalIgnoreCase))
            return ConvertResult.Ok(amount, 1m, "local");

        ExchangeRateApiResponse? data;
        try
        {
            data = await GetLatestAsync(fromCurrency.ToUpperInvariant());
        }
        catch (Exception ex)
        {
            return ConvertResult.Fail($"Не удалось получить курс онлайн: {ex.Message}");
        }

        if (!string.Equals(data.Result, "success", StringComparison.OrdinalIgnoreCase))
            return ConvertResult.Fail("API вернул неуспешный результат.");

        if (data.Rates is null || !data.Rates.TryGetValue("KZT", out var rateToKzt))
            return ConvertResult.Fail("В ответе API нет курса KZT.");

        var amountKzt = amount * rateToKzt;
        return ConvertResult.Ok(amountKzt, rateToKzt, data.TimeLastUpdateUtc ?? "unknown");
    }

    private async Task<ExchangeRateApiResponse> GetLatestAsync(string baseCurrency)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(baseCurrency, out var cached))
            {
                if (DateTimeOffset.UtcNow - cached.fetchedAt < TimeSpan.FromMinutes(10))
                    return cached.data;
            }
        }

        var url = $"https://open.er-api.com/v6/latest/{baseCurrency}";
        using var resp = await _http.GetAsync(url);
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadAsStringAsync();

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        var data = JsonSerializer.Deserialize<ExchangeRateApiResponse>(json, options)
                   ?? throw new Exception("Пустой/непонятный ответ от API.");

        lock (_lock)
        {
            _cache[baseCurrency] = (DateTimeOffset.UtcNow, data);
        }

        return data;
    }
}

public sealed class ExchangeRateApiResponse
{
    [JsonPropertyName("result")]
    public string? Result { get; set; }

    [JsonPropertyName("base_code")]
    public string? BaseCode { get; set; }

    [JsonPropertyName("time_last_update_utc")]
    public string? TimeLastUpdateUtc { get; set; }

    [JsonPropertyName("rates")]
    public Dictionary<string, decimal>? Rates { get; set; }
}

public sealed class ConvertResult
{
    public bool Success { get; init; }
    public decimal AmountKzt { get; init; }
    public decimal RateToKzt { get; init; }
    public string LastUpdateUtc { get; init; } = "unknown";
    public string ErrorMessage { get; init; } = "";

    public static ConvertResult Ok(decimal amountKzt, decimal rateToKzt, string lastUpdateUtc) =>
        new ConvertResult
        {
            Success = true,
            AmountKzt = amountKzt,
            RateToKzt = rateToKzt,
            LastUpdateUtc = lastUpdateUtc
        };

    public static ConvertResult Fail(string message) =>
        new ConvertResult
        {
            Success = false,
            ErrorMessage = message
        };
}
