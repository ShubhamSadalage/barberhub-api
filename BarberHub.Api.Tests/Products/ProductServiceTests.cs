using BarberHub.Api.Domain.Entities;
using BarberHub.Api.Features.Products.Dtos;
using BarberHub.Api.Features.Products.Repositories;
using BarberHub.Api.Features.Products.Services;
using FluentAssertions;
using Moq;
using Xunit;

namespace BarberHub.Api.Tests.Products;

public class ProductServiceTests
{
    private readonly Mock<IProductRepository> _repoMock;
    private readonly ProductService _sut;

    public ProductServiceTests()
    {
        _repoMock = new Mock<IProductRepository>();
        _sut = new ProductService(_repoMock.Object);
    }

    [Fact]
    public async Task CreateAsync_WithCostPrice_PersistsCostPrice()
    {
        // Arrange
        Product? addedProduct = null;
        _repoMock.Setup(r => r.AddAsync(It.IsAny<Product>()))
            .Callback<Product>(p => addedProduct = p)
            .Returns(Task.CompletedTask);
        _repoMock.Setup(r => r.SaveChangesAsync()).ReturnsAsync(1);

        var dto = new CreateProductDto
        {
            Name = "Matte Hair Clay",
            Description = "Strong matte hold",
            Price = 499m,
            CostPrice = 220m, // Buying / Wholesale cost
            StockQuantity = 40,
            Category = "Styling",
            DiscountPercentage = 10m
        };

        // Act
        var result = await _sut.CreateAsync("barber-123", dto, null);

        // Assert
        result.IsSuccess.Should().BeTrue();
        addedProduct.Should().NotBeNull();
        addedProduct!.Name.Should().Be("Matte Hair Clay");
        addedProduct.Price.Should().Be(499m);
        addedProduct.CostPrice.Should().Be(220m);
        addedProduct.BarberId.Should().Be("barber-123");
    }

    [Fact]
    public async Task GetByIdAsync_CustomerFacing_DoesNotExposeCostPrice()
    {
        // Arrange
        var product = new Product
        {
            Id = Guid.NewGuid(),
            Name = "Luxury Beard Balm",
            Price = 600m,
            CostPrice = 280m, // Secret cost price
            StockQuantity = 15,
            Category = "Beard Care",
            BarberId = "barber-123",
            IsActive = true
        };
        _repoMock.Setup(r => r.GetByIdAsync(product.Id)).ReturnsAsync(product);

        // Act
        var result = await _sut.GetByIdAsync(product.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("Luxury Beard Balm");
        result.Price.Should().Be(600m);
        result.EffectivePrice.Should().Be(600m);
        // ProductListItemDto does not have CostPrice property at all (strictly confidential)
        typeof(ProductListItemDto).GetProperty("CostPrice").Should().BeNull();
    }

    [Fact]
    public async Task GetForEditAsync_AsOwningBarber_ReturnsCostPrice()
    {
        // Arrange
        var productId = Guid.NewGuid();
        var product = new Product
        {
            Id = productId,
            Name = "Luxury Beard Balm",
            Price = 600m,
            CostPrice = 280m,
            StockQuantity = 15,
            Category = "Beard Care",
            BarberId = "barber-123",
            IsActive = true
        };
        _repoMock.Setup(r => r.GetByIdAsync(productId)).ReturnsAsync(product);

        // Act
        var result = await _sut.GetForEditAsync("barber-123", productId);

        // Assert
        result.Should().NotBeNull();
        result!.CostPrice.Should().Be(280m);
        result.Price.Should().Be(600m);
        result.Name.Should().Be("Luxury Beard Balm");
    }

    [Fact]
    public async Task GetForEditAsync_AsDifferentBarber_ReturnsNullUnauthorized()
    {
        // Arrange
        var productId = Guid.NewGuid();
        var product = new Product
        {
            Id = productId,
            Name = "Luxury Beard Balm",
            Price = 600m,
            CostPrice = 280m,
            BarberId = "barber-123"
        };
        _repoMock.Setup(r => r.GetByIdAsync(productId)).ReturnsAsync(product);

        // Act
        var result = await _sut.GetForEditAsync("different-barber-456", productId);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_ModifiesCostPrice()
    {
        // Arrange
        var productId = Guid.NewGuid();
        var product = new Product
        {
            Id = productId,
            Name = "Hair Wax",
            Price = 400m,
            CostPrice = 180m,
            StockQuantity = 20,
            Category = "Styling",
            BarberId = "barber-123",
            IsActive = true
        };
        _repoMock.Setup(r => r.GetByIdAsync(productId)).ReturnsAsync(product);
        _repoMock.Setup(r => r.SaveChangesAsync()).ReturnsAsync(1);

        var editDto = new EditProductDto
        {
            Id = productId,
            Name = "Hair Wax (Updated)",
            Price = 450m,
            CostPrice = 210m, // Updated wholesale price
            StockQuantity = 35,
            Category = "Styling",
            IsActive = true
        };

        // Act
        var result = await _sut.UpdateAsync("barber-123", editDto, null);

        // Assert
        result.IsSuccess.Should().BeTrue();
        product.Price.Should().Be(450m);
        product.CostPrice.Should().Be(210m);
        product.StockQuantity.Should().Be(35);
    }
}
