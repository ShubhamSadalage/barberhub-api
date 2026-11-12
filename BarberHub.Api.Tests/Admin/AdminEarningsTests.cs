using BarberHub.Api.Domain.Entities;
using BarberHub.Api.Features.Admin;
using BarberHub.Api.Features.Notifications;
using BarberHub.Api.Infrastructure.Persistence;
using BarberHub.Api.Shared.Config;
using BarberHub.Api.Shared.Services;
using BarberHub.Api.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace BarberHub.Api.Tests.Admin;

public class AdminEarningsTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly Mock<UserManager<ApplicationUser>> _userManagerMock;
    private readonly Mock<ICurrentUserService> _currentUserMock;
    private readonly Mock<INotificationService> _notificationsMock;
    private readonly IOptions<FeatureFlags> _features;
    private readonly AdminController _sut;

    public AdminEarningsTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new ApplicationDbContext(options);

        _userManagerMock = MockHelpers.CreateUserManager();
        _currentUserMock = new Mock<ICurrentUserService>();
        _notificationsMock = new Mock<INotificationService>();
        _features = Options.Create(new FeatureFlags
        {
            EarningsFinancialsEnabled = true,
            ProductsEnabled = true,
            OnlinePaymentEnabled = true
        });

        _currentUserMock.Setup(c => c.UserId).Returns("barber-100");

        _sut = new AdminController(
            _context,
            _userManagerMock.Object,
            _currentUserMock.Object,
            _notificationsMock.Object,
            _features);
    }

    [Fact]
    public async Task Earnings_WithServiceAndProductSales_CalculatesDualTabMetricsAccurately()
    {
        // Arrange
        var barberId = "barber-100";
        var today = DateOnly.FromDateTime(DateTime.Today);

        // 1. Service bookings
        var service1 = new Service { Id = Guid.NewGuid(), Name = "Fade Haircut", Price = 300m, BarberId = barberId, DurationMinutes = 30 };
        var service2 = new Service { Id = Guid.NewGuid(), Name = "Beard Styling", Price = 200m, BarberId = barberId, DurationMinutes = 20 };
        await _context.Services.AddRangeAsync(service1, service2);

        var booking1 = new Booking
        {
            Id = Guid.NewGuid(),
            BarberId = barberId,
            UserId = "user-1",
            ServiceId = service1.Id,
            AppointmentDate = today,
            StartTime = new TimeOnly(10, 0),
            EndTime = new TimeOnly(10, 30),
            Status = BookingStatus.Completed,
            TotalAmount = 300m
        };
        var booking2 = new Booking
        {
            Id = Guid.NewGuid(),
            BarberId = barberId,
            UserId = "user-2",
            ServiceId = service2.Id,
            AppointmentDate = today,
            StartTime = new TimeOnly(11, 0),
            EndTime = new TimeOnly(11, 20),
            Status = BookingStatus.Accepted,
            TotalAmount = 200m
        };
        await _context.Bookings.AddRangeAsync(booking1, booking2);

        // 2. Products and Orders
        var prod1 = new Product
        {
            Id = Guid.NewGuid(),
            Name = "Hair Wax",
            Price = 500m,
            CostPrice = 250m, // Wholesale buying cost
            StockQuantity = 45,
            BarberId = barberId,
            Category = "Styling"
        };
        var prod2 = new Product
        {
            Id = Guid.NewGuid(),
            Name = "Beard Oil",
            Price = 400m,
            CostPrice = 150m, // Wholesale buying cost
            StockQuantity = 20,
            BarberId = barberId,
            Category = "Beard Care"
        };
        await _context.Products.AddRangeAsync(prod1, prod2);

        var order1 = new Order
        {
            Id = Guid.NewGuid(),
            OrderNumber = "ORD-001",
            UserId = "user-1",
            Status = OrderStatus.Paid,
            TotalAmount = 1500m,
            CreatedAt = DateTime.UtcNow
        };
        var order2 = new Order
        {
            Id = Guid.NewGuid(),
            OrderNumber = "ORD-002",
            UserId = "user-2",
            Status = OrderStatus.Delivered,
            TotalAmount = 800m,
            CreatedAt = DateTime.UtcNow
        };
        await _context.Orders.AddRangeAsync(order1, order2);

        // Order 1 has 2 Hair Wax (Unit Price 500, Unit Cost 250)
        var item1 = new OrderItem
        {
            Id = Guid.NewGuid(),
            OrderId = order1.Id,
            ProductId = prod1.Id,
            ProductName = prod1.Name,
            Quantity = 2,
            UnitPrice = 500m,
            UnitCostPrice = 250m
        };
        // Order 2 has 2 Beard Oil (Unit Price 400, Unit Cost 150)
        var item2 = new OrderItem
        {
            Id = Guid.NewGuid(),
            OrderId = order2.Id,
            ProductId = prod2.Id,
            ProductName = prod2.Name,
            Quantity = 2,
            UnitPrice = 400m,
            UnitCostPrice = 150m
        };
        await _context.OrderItems.AddRangeAsync(item1, item2);
        await _context.SaveChangesAsync();

        // Act
        var actionResult = await _sut.Earnings();
        var okResult = actionResult as OkObjectResult;
        var dto = okResult?.Value as EarningsDto;

        // Assert
        dto.Should().NotBeNull();

        // 1. Service metrics
        dto!.ServiceTotalEarnings.Should().Be(500m); // 300 + 200
        dto.ServiceTodayEarnings.Should().Be(500m);
        dto.ServiceTotalCompleted.Should().Be(1);
        dto.ServiceTotalAccepted.Should().Be(1);
        dto.ByService.Should().HaveCount(2);

        // 2. Product Financials
        // Revenue = (2 * 500) + (2 * 400) = 1000 + 800 = 1800
        dto.ProductTotalRevenue.Should().Be(1800m);
        // Cost = (2 * 250) + (2 * 150) = 500 + 300 = 800
        dto.ProductTotalCost.Should().Be(800m);
        // Net Profit = 1800 - 800 = 1000
        dto.ProductNetProfit.Should().Be(1000m);
        // Profit Margin = (1000 / 1800) * 100 = 55.56%
        dto.ProductProfitMarginPercentage.Should().Be(55.56m);
        dto.ProductTotalUnitsSold.Should().Be(4);
        dto.ProductTotalOrders.Should().Be(2);

        // Product Breakdown
        dto.ByProduct.Should().HaveCount(2);
        var waxRow = dto.ByProduct.First(p => p.ProductName == "Hair Wax");
        waxRow.UnitsSold.Should().Be(2);
        waxRow.TotalRevenue.Should().Be(1000m);
        waxRow.TotalCost.Should().Be(500m);
        waxRow.NetProfit.Should().Be(500m);
        waxRow.ProfitMarginPercentage.Should().Be(50m);
        waxRow.CostPrice.Should().Be(250m);

        var oilRow = dto.ByProduct.First(p => p.ProductName == "Beard Oil");
        oilRow.UnitsSold.Should().Be(2);
        oilRow.TotalRevenue.Should().Be(800m);
        oilRow.TotalCost.Should().Be(300m);
        oilRow.NetProfit.Should().Be(500m);
        oilRow.ProfitMarginPercentage.Should().Be(62.5m);
        oilRow.CostPrice.Should().Be(150m);

        // 3. Combined Overview
        dto.CombinedTotalRevenue.Should().Be(2300m); // 500 + 1800
        dto.CombinedTotalProfit.Should().Be(1500m);  // 500 + 1000
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }
}
