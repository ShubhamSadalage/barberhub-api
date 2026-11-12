using BarberHub.Api.Domain.Constants;
using BarberHub.Api.Domain.Entities;
using BarberHub.Api.Features.Barbers.Dtos;
using BarberHub.Api.Features.Barbers.Repositories;
using BarberHub.Api.Features.Barbers.Services;
using BarberHub.Api.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Moq;
using Xunit;

namespace BarberHub.Api.Tests.Barbers;

public class BarberServiceTests
{
    private readonly Mock<IBarberRepository> _repoMock;
    private readonly Mock<UserManager<ApplicationUser>> _userManagerMock;
    private readonly List<ApplicationUser> _users;
    private readonly BarberService _sut;

    public BarberServiceTests()
    {
        _repoMock = new Mock<IBarberRepository>();
        _users = new List<ApplicationUser>();
        _userManagerMock = MockHelpers.CreateUserManager(_users);

        _sut = new BarberService(
            _repoMock.Object,
            _userManagerMock.Object,
            MockHelpers.CreateLogger<BarberService>());
    }

    [Fact]
    public async Task GetForHomeAsync_WithLiveCoordinatesNearBarbers_ReturnsLiveLocationSource()
    {
        // Arrange (User near Pune: 18.5204, 73.8567)
        var barber = new ApplicationUser
        {
            Id = "b1",
            FullName = "Pune Barber",
            ShopName = "Pune Salon",
            Latitude = 18.5210,
            Longitude = 73.8570,
            IsActive = true,
            ApprovalStatus = BarberApprovalStatus.Approved
        };

        _repoMock.Setup(r => r.SearchAsync(null))
            .ReturnsAsync(new List<ApplicationUser> { barber });
        _repoMock.Setup(r => r.GetServicesAsync("b1"))
            .ReturnsAsync(new List<Service>
            {
                new() { Id = Guid.NewGuid(), Name = "Haircut", Price = 250, BarberId = "b1", IsActive = true }
            });

        // Act
        var result = await _sut.GetForHomeAsync(null, 18.5204, 73.8567);

        // Assert
        result.Source.Should().Be(HomeBarberSource.LiveLocation);
        result.Items.Should().HaveCount(1);
        result.Items[0].ShopName.Should().Be("Pune Salon");
        result.Items[0].DistanceKm.Should().BeLessThan(1.0);
    }

    [Fact]
    public async Task GetForHomeAsync_WithLiveCoordinatesFarAway_PreservesCoordinatesInFallbackAndCalculatesDistance()
    {
        // Arrange (User in Mumbai: 19.0760, 72.8777, Barber in Pune: 18.5204, 73.8567 ~ 120km away)
        var barber = new ApplicationUser
        {
            Id = "b1",
            FullName = "Pune Barber",
            ShopName = "Pune Salon",
            Latitude = 18.5204,
            Longitude = 73.8567,
            IsActive = true,
            ApprovalStatus = BarberApprovalStatus.Approved
        };

        _repoMock.Setup(r => r.SearchAsync(null))
            .ReturnsAsync(new List<ApplicationUser> { barber });
        _repoMock.Setup(r => r.GetServicesAsync("b1"))
            .ReturnsAsync(new List<Service>());

        // Act
        var result = await _sut.GetForHomeAsync(null, 19.0760, 72.8777);

        // Assert
        result.Source.Should().Be(HomeBarberSource.Featured);
        result.Items.Should().HaveCount(1);
        result.Items[0].DistanceKm.Should().NotBeNull();
        result.Items[0].DistanceKm!.Value.Should().BeInRange(100, 150); // ~120km
    }

    [Fact]
    public async Task GetForHomeAsync_WithSavedUserLocation_ReturnsSavedLocationSource()
    {
        // Arrange
        var user = new ApplicationUser
        {
            Id = "cust1",
            FullName = "Customer",
            Latitude = 18.5204,
            Longitude = 73.8567,
            LocationLabel = "Home"
        };
        _users.Add(user);

        var barber = new ApplicationUser
        {
            Id = "b1",
            FullName = "Pune Barber",
            Latitude = 18.5205,
            Longitude = 73.8568,
            IsActive = true,
            ApprovalStatus = BarberApprovalStatus.Approved
        };

        _repoMock.Setup(r => r.SearchAsync(null))
            .ReturnsAsync(new List<ApplicationUser> { barber });
        _repoMock.Setup(r => r.GetServicesAsync("b1"))
            .ReturnsAsync(new List<Service>());

        // Act
        var result = await _sut.GetForHomeAsync("cust1", null, null);

        // Assert
        result.Source.Should().Be(HomeBarberSource.SavedLocation);
        result.Label.Should().Be("Home");
        result.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetForHomeAsync_WithUserCity_ReturnsCitySource()
    {
        // Arrange
        var user = new ApplicationUser
        {
            Id = "cust2",
            FullName = "Customer",
            City = "Nagpur"
        };
        _users.Add(user);

        var barber = new ApplicationUser
        {
            Id = "b2",
            FullName = "Nagpur Barber",
            City = "Nagpur",
            IsActive = true,
            ApprovalStatus = BarberApprovalStatus.Approved
        };

        _repoMock.Setup(r => r.SearchAsync("Nagpur"))
            .ReturnsAsync(new List<ApplicationUser> { barber });
        _repoMock.Setup(r => r.GetServicesAsync("b2"))
            .ReturnsAsync(new List<Service>());

        // Act
        var result = await _sut.GetForHomeAsync("cust2", null, null);

        // Assert
        result.Source.Should().Be(HomeBarberSource.City);
        result.Label.Should().Be("Nagpur");
        result.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetDetailsAsync_CorrectlyMapsActiveServicesWithIsActiveAndCategory()
    {
        // Arrange
        var serviceId = Guid.NewGuid();
        var barber = new ApplicationUser
        {
            Id = "b1",
            FullName = "Master Barber",
            ShopName = "Elite Cuts",
            Services = new List<Service>
            {
                new()
                {
                    Id = serviceId,
                    Name = "Beard Trim",
                    Category = "Beard",
                    Price = 150,
                    DurationMinutes = 20,
                    IsActive = true,
                    BarberId = "b1"
                }
            }
        };

        _repoMock.Setup(r => r.GetBarberWithDetailsAsync("b1")).ReturnsAsync(barber);
        _repoMock.Setup(r => r.GetLeaveDatesAsync("b1", It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(new List<DateOnly>());

        // Act
        var details = await _sut.GetDetailsAsync("b1");

        // Assert
        details.Should().NotBeNull();
        details!.Services.Should().HaveCount(1);
        var svc = details.Services.First();
        svc.Id.Should().Be(serviceId);
        svc.Name.Should().Be("Beard Trim");
        svc.Category.Should().Be("Beard");
        svc.Price.Should().Be(150);
        svc.DurationMinutes.Should().Be(20);
        svc.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task GetAvailableSlotsAsync_BarberWeeklyHoliday_ReturnsIsHolidayTrue()
    {
        // Arrange: Barber closed on Mondays
        var monday = new DateOnly(2026, 9, 14); // Sep 14, 2026 is Monday
        var barber = new ApplicationUser
        {
            Id = "b1",
            WeeklyHoliday = DayOfWeek.Monday,
            OpeningTime = new TimeOnly(9, 0),
            ClosingTime = new TimeOnly(20, 0),
            Services = new List<Service>()
        };

        _repoMock.Setup(r => r.GetBarberWithDetailsAsync("b1")).ReturnsAsync(barber);

        // Act
        var result = await _sut.GetAvailableSlotsAsync("b1", monday, Guid.NewGuid());

        // Assert
        result.IsHoliday.Should().BeTrue();
        result.AvailableSlots.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAvailableSlotsAsync_BarberLeaveDay_ReturnsIsLeaveDayTrue()
    {
        // Arrange
        var leaveDate = new DateOnly(2026, 9, 15);
        var barber = new ApplicationUser
        {
            Id = "b1",
            WeeklyHoliday = DayOfWeek.Sunday,
            OpeningTime = new TimeOnly(9, 0),
            ClosingTime = new TimeOnly(20, 0),
            Services = new List<Service>()
        };

        _repoMock.Setup(r => r.GetBarberWithDetailsAsync("b1")).ReturnsAsync(barber);
        _repoMock.Setup(r => r.GetLeaveDatesAsync("b1", leaveDate, leaveDate))
            .ReturnsAsync(new List<DateOnly> { leaveDate });

        // Act
        var result = await _sut.GetAvailableSlotsAsync("b1", leaveDate, Guid.NewGuid());

        // Assert
        result.IsLeaveDay.Should().BeTrue();
        result.AvailableSlots.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAvailableSlotsAsync_WithAcceptedAndQueuedBookings_ClassifiesStatusesCorrectly()
    {
        // Arrange
        var targetDate = new DateOnly(2026, 9, 16); // Wednesday
        var svcId = Guid.NewGuid();
        var barber = new ApplicationUser
        {
            Id = "b1",
            OpeningTime = new TimeOnly(10, 0),
            ClosingTime = new TimeOnly(12, 0), // 10:00, 10:30, 11:00, 11:30
            Services = new List<Service>
            {
                new() { Id = svcId, DurationMinutes = 30 }
            }
        };

        var bookings = new List<Booking>
        {
            // 10:00 is Accepted -> "Booked"
            new() { BarberId = "b1", AppointmentDate = targetDate, StartTime = new TimeOnly(10, 0), EndTime = new TimeOnly(10, 30), Status = BookingStatus.Accepted },
            // 10:30 has 1 Pending -> "Queued"
            new() { BarberId = "b1", AppointmentDate = targetDate, StartTime = new TimeOnly(10, 30), EndTime = new TimeOnly(11, 0), Status = BookingStatus.Pending },
            // 11:00 has 3 Pending -> "Booked"
            new() { BarberId = "b1", AppointmentDate = targetDate, StartTime = new TimeOnly(11, 0), EndTime = new TimeOnly(11, 30), Status = BookingStatus.Pending },
            new() { BarberId = "b1", AppointmentDate = targetDate, StartTime = new TimeOnly(11, 0), EndTime = new TimeOnly(11, 30), Status = BookingStatus.Pending },
            new() { BarberId = "b1", AppointmentDate = targetDate, StartTime = new TimeOnly(11, 0), EndTime = new TimeOnly(11, 30), Status = BookingStatus.Pending }
            // 11:30 is Free -> "Available"
        };

        _repoMock.Setup(r => r.GetBarberWithDetailsAsync("b1")).ReturnsAsync(barber);
        _repoMock.Setup(r => r.GetLeaveDatesAsync("b1", targetDate, targetDate)).ReturnsAsync(new List<DateOnly>());
        _repoMock.Setup(r => r.GetBookingsForDateAsync("b1", targetDate)).ReturnsAsync(bookings);

        // Act
        var result = await _sut.GetAvailableSlotsAsync("b1", targetDate, svcId);

        // Assert
        result.SlotStatuses.Should().HaveCount(4);

        var slot1000 = result.SlotStatuses.First(s => s.Time == new TimeOnly(10, 0));
        slot1000.Status.Should().Be("Booked");

        var slot1030 = result.SlotStatuses.First(s => s.Time == new TimeOnly(10, 30));
        slot1030.Status.Should().Be("Queued");
        slot1030.QueueCount.Should().Be(1);

        var slot1100 = result.SlotStatuses.First(s => s.Time == new TimeOnly(11, 0));
        slot1100.Status.Should().Be("Booked");

        var slot1130 = result.SlotStatuses.First(s => s.Time == new TimeOnly(11, 30));
        slot1130.Status.Should().Be("Available");

        // AvailableSlots list includes Available and Queued (<3) slots
        result.AvailableSlots.Should().Contain(new TimeOnly(10, 30));
        result.AvailableSlots.Should().Contain(new TimeOnly(11, 30));
        result.AvailableSlots.Should().NotContain(new TimeOnly(10, 0));
        result.AvailableSlots.Should().NotContain(new TimeOnly(11, 0));
    }

    [Fact]
    public async Task GetNextAvailableSlotAsync_FindsFirstAvailableSlotAfterGivenTime()
    {
        // Arrange
        var targetDate = new DateOnly(2026, 9, 16);
        var svcId = Guid.NewGuid();
        var barber = new ApplicationUser
        {
            Id = "b1",
            OpeningTime = new TimeOnly(10, 0),
            ClosingTime = new TimeOnly(12, 0),
            Services = new List<Service> { new() { Id = svcId, DurationMinutes = 30 } }
        };

        var bookings = new List<Booking>
        {
            new() { BarberId = "b1", AppointmentDate = targetDate, StartTime = new TimeOnly(10, 0), EndTime = new TimeOnly(10, 30), Status = BookingStatus.Accepted },
            new() { BarberId = "b1", AppointmentDate = targetDate, StartTime = new TimeOnly(10, 30), EndTime = new TimeOnly(11, 0), Status = BookingStatus.Accepted }
        };

        _repoMock.Setup(r => r.GetBarberWithDetailsAsync("b1")).ReturnsAsync(barber);
        _repoMock.Setup(r => r.GetLeaveDatesAsync("b1", targetDate, targetDate)).ReturnsAsync(new List<DateOnly>());
        _repoMock.Setup(r => r.GetBookingsForDateAsync("b1", targetDate)).ReturnsAsync(bookings);

        // Act (looking for slot after 10:30)
        var nextSlot = await _sut.GetNextAvailableSlotAsync("b1", targetDate, svcId, new TimeOnly(10, 30));

        // Assert
        nextSlot.Should().Be(new TimeOnly(11, 0));
    }
}
