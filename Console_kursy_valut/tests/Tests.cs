using Xunit;

public class ExchangeRateTests
{
    [Fact]
    public void Test_ConvertResult_Ok()
    {
        // Arrange
        decimal amount = 100;
        decimal rate = 450;
        decimal expected = amount * rate;

        // Act
        var result = ConvertResult.Ok(expected, rate, "test");

        // Assert
        Assert.True(result.Success);
        Assert.Equal(expected, result.AmountKzt);
        Assert.Equal(rate, result.RateToKzt);
        Assert.Equal("test", result.LastUpdateUtc);
    }

    [Fact]
    public void Test_ConvertResult_Fail()
    {
        var result = ConvertResult.Fail("Ошибка");

        Assert.False(result.Success);
        Assert.Equal("Ошибка", result.ErrorMessage);
    }

    [Fact]
    public void Test_Product_Creation()
    {
        var product = new Product("Mouse", 10m, "USD");
        Assert.Equal("Mouse", product.Name);
        Assert.Equal(10m, product.Price);
        Assert.Equal("USD", product.Currency);
    }

    [Fact]
    public void Test_ExchangeRateService_InvalidAmount()
    {
        var http = new HttpClient();
        var service = new ExchangeRateService(http);

        var task = service.ConvertToKztAsync(-10, "USD");
        var result = task.Result;

        Assert.False(result.Success);
        Assert.Contains("Сумма должна быть", result.ErrorMessage);
    }
}
