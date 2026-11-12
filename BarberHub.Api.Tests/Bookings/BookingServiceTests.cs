using BarberHub.Api.Domain.Constants;
using BarberHub.Api.Domain.Entities;
using BarberHub.Api.Features.Barbers.Services;
using BarberHub.Api.Features.Bookings.Dtos;
using BarberHub.Api.Features.Bookings.Repositories;
using BarberHub.Api.Features.Bookings.Services;
using BarberHub.Api.Features.Notifications;
using BarberHub.Api.Infrastructure.Persistence;
using BarberHub.Api.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Moq;
using Xunit;

namespace BarberHub.Api.Tests.Bookings;

public class BookingServiceTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly BookingRepository _repo;
    private readonly Mock<INotificationService> _notificationsMock;
    private readonly Mock<IBarberService> _barberServiceMock;
    private readonly Mock<IHubContext<NotificationHub>> _hubMock;
    private readonly Mock<IClientProxy> _clientProxyMock;
    private readonly BookingService _sut;

    private readonly ApplicationUser _barber;
    private readonly ApplicationUser _customer1;
    private readonly ApplicationUser _customer2;
    private readonly Service _service;

    public BookingServiceTests()
    {
        _db = TestDbContextFactory.Create();
        _repo = new BookingRepository(_db);
        _notificationsMock = new Mock<INotificationService>();
        _barberServiceMock = new Mock<IBarberService>();

        _hubMock = new Mock<IHubContext<NotificationHub>>();
        var hubClients = new Mock<IHubClients>();
        _clientProxyMock = new Mock<IClientProxy>();
        _hubMock.Setup(h => h.Clients).Returns(hubClients.Object);
        hubClients.Setup(c => c.User(It.IsAny<string>())).Returns(_clientProxyMock.Object);

        _sut = new BookingService(
            _repo,
            _db,
            _notificationsMock.Object,
            _barberServiceMock.Object,
            _hubMock.Object);

        // Seed common entities
        _barber = new ApplicationUser
        {
            Id = "barber-1",
            FullName = "Master Barber",
            ShopName = "Royal Salon",
            City = "Pune",
            WeeklyHoliday = DayOfWeek.Sunday,
            OpeningTime = new TimeOnly(9, 0),
            ClosingTime = new TimeOnly(20, 0),
            IsActive = true,
            ApprovalStatus = BarberApprovalStatus.Approved
        };

        _customer1 = new ApplicationUser
        {
            Id = "cust-1",
            FullName = "John Doe",
            Email = "john@test.com",
            PhoneNumber = "9876543210"
        };

        _customer2 = new ApplicationUser
        {
            Id = "cust-2",
            FullName = "Jane Smith",
            Email = "jane@test.com",
            PhoneNumber = "9876543211"
        };

        _service = new Service
        {
            Id = Guid.NewGuid(),
            BarberId = _barber.Id,
            Name = "Haircut & Beard",
            Price = 350,
            DurationMinutes = 30,
            IsActive = true
        };

        _db.Users.AddRange(_barber, _customer1, _customer2);
        _db.Services.Add(_service);
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Database.EnsureDeleted();
        _db.Dispose();
    }

    [Fact]
    public async Task CreateAsync_SingleBooking_ReturnsQueuePosition1()
    {
        // Arrange (Future date: Wednesday)
        var date = DateOnly.FromDateTime(DateTime.Today.AddDays(2));
        var start = new TimeOnly(10, 0);

        var dto = new CreateBookingDto
        {
            BarberId = _barber.Id,
            ServiceId = _service.Id,
            AppointmentDate = date,
            StartTime = start,
            Notes = "First time customer"
        };

        // Act
        var result = await _sut.CreateAsync(_customer1.Id, dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.SlotQueuePosition.Should().Be(1);
        result.Value.IsQueued.Should().BeFalse();

        var bookingInDb = _db.Bookings.FirstOrDefault(b => b.Id == result.Value.BookingId);
        bookingInDb.Should().NotBeNull();
        bookingInDb!.Status.Should().Be(BookingStatus.Pending);
        bookingInDb.EndTime.Should().Be(new TimeOnly(10, 30));

        // Verify Barber notified
        _notificationsMock.Verify(n => n.NotifyAsync(
            _barber.Id,
            NotificationType.BookingCreated,
            "New Booking Request",
            It.IsAny<string>(),
            "/Bookings/Manage"), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_CompetingConcurrentSlot_StacksInQueuePosition2()
    {
        // Arrange
        var date = DateOnly.FromDateTime(DateTime.Today.AddDays(2));
        var start = new TimeOnly(11, 0);

        // First customer books 11:00
        var firstResult = await _sut.CreateAsync(_customer1.Id, new CreateBookingDto
        {
            BarberId = _barber.Id,
            ServiceId = _service.Id,
            AppointmentDate = date,
            StartTime = start
        });
        firstResult.IsSuccess.Should().BeTrue();
        firstResult.Value!.SlotQueuePosition.Should().Be(1);

        // Second customer attempts to book the EXACT SAME 11:00 slot
        var secondResult = await _sut.CreateAsync(_customer2.Id, new CreateBookingDto
        {
            BarberId = _barber.Id,
            ServiceId = _service.Id,
            AppointmentDate = date,
            StartTime = start
        });

        // Assert
        secondResult.IsSuccess.Should().BeTrue();
        secondResult.Value!.SlotQueuePosition.Should().Be(2);
        secondResult.Value.IsQueued.Should().BeTrue();
        secondResult.Value.Message.Should().Contain("#2 in queue");

        // Verify Barber notified about queue #2
        _notificationsMock.Verify(n => n.NotifyAsync(
            _barber.Id,
            NotificationType.BookingCreated,
            "[Queue #2] New Booking Request",
            It.IsAny<string>(),
            "/Bookings/Manage"), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_WhenQueueStackIsFull_ReturnsFailure()
    {
        // Arrange (Add 3 pending bookings for same slot)
        var date = DateOnly.FromDateTime(DateTime.Today.AddDays(2));
        var start = new TimeOnly(14, 0);
        var end = new TimeOnly(14, 30);

        for (int i = 0; i < 3; i++)
        {
            _db.Bookings.Add(new Booking
            {
                UserId = _customer1.Id,
                BarberId = _barber.Id,
                ServiceId = _service.Id,
                AppointmentDate = date,
                StartTime = start,
                EndTime = end,
                Status = BookingStatus.Pending,
                CreatedAt = DateTime.UtcNow.AddMinutes(i)
            });
        }
        await _db.SaveChangesAsync();

        // Act (4th customer attempts to book)
        var result = await _sut.CreateAsync(_customer2.Id, new CreateBookingDto
        {
            BarberId = _barber.Id,
            ServiceId = _service.Id,
            AppointmentDate = date,
            StartTime = start
        });

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("maximum queue limit reached");
    }

    [Fact]
    public async Task CreateAsync_WhenSlotAlreadyAccepted_ReturnsFailure()
    {
        // Arrange
        var date = DateOnly.FromDateTime(DateTime.Today.AddDays(2));
        var start = new TimeOnly(15, 0);
        var end = new TimeOnly(15, 30);

        _db.Bookings.Add(new Booking
        {
            UserId = _customer1.Id,
            BarberId = _barber.Id,
            ServiceId = _service.Id,
            AppointmentDate = date,
            StartTime = start,
            EndTime = end,
            Status = BookingStatus.Accepted
        });
        await _db.SaveChangesAsync();

        // Act
        var result = await _sut.CreateAsync(_customer2.Id, new CreateBookingDto
        {
            BarberId = _barber.Id,
            ServiceId = _service.Id,
            AppointmentDate = date,
            StartTime = start
        });

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("already been confirmed for another customer");
    }

    [Fact]
    public async Task CreateAsync_OnBarberLeaveDay_ReturnsFailure()
    {
        // Arrange
        var leaveDate = DateOnly.FromDateTime(DateTime.Today.AddDays(3));
        _db.LeaveDays.Add(new LeaveDay
        {
            BarberId = _barber.Id,
            LeaveDate = leaveDate,
            Reason = "Personal leave"
        });
        await _db.SaveChangesAsync();

        // Act
        var result = await _sut.CreateAsync(_customer1.Id, new CreateBookingDto
        {
            BarberId = _barber.Id,
            ServiceId = _service.Id,
            AppointmentDate = leaveDate,
            StartTime = new TimeOnly(10, 0)
        });

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("on leave");
    }

    [Fact]
    public async Task UpdateStatusAsync_BarberAcceptsPrimary_RejectsCompetitorAndDispatchesSlotTakenOfferNext()
    {
        // Arrange: 2 competing pending bookings for 16:00 slot
        var date = DateOnly.FromDateTime(DateTime.Today.AddDays(2));
        var start = new TimeOnly(16, 0);
        var end = new TimeOnly(16, 30);

        var booking1 = new Booking
        {
            Id = Guid.NewGuid(),
            UserId = _customer1.Id,
            BarberId = _barber.Id,
            ServiceId = _service.Id,
            AppointmentDate = date,
            StartTime = start,
            EndTime = end,
            Status = BookingStatus.Pending,
            CreatedAt = DateTime.UtcNow.AddMinutes(-10),
            TotalAmount = 350
        };

        var booking2 = new Booking
        {
            Id = Guid.NewGuid(),
            UserId = _customer2.Id,
            BarberId = _barber.Id,
            ServiceId = _service.Id,
            AppointmentDate = date,
            StartTime = start,
            EndTime = end,
            Status = BookingStatus.Pending,
            CreatedAt = DateTime.UtcNow.AddMinutes(-5),
            TotalAmount = 350
        };

        _db.Bookings.AddRange(booking1, booking2);
        await _db.SaveChangesAsync();

        // Next available slot calculation returns 16:30
        _barberServiceMock.Setup(b => b.GetNextAvailableSlotAsync(
            _barber.Id, date, _service.Id, end))
            .ReturnsAsync(new TimeOnly(16, 30));

        // Act: Barber accepts booking1
        var updateResult = await _sut.UpdateStatusAsync(_barber.Id, new UpdateBookingStatusDto
        {
            BookingId = booking1.Id,
            NewStatus = BookingStatus.Accepted
        });

        // Assert
        updateResult.IsSuccess.Should().BeTrue();

        // Booking 1 should be Accepted
        var reloaded1 = await _db.Bookings.FindAsync(booking1.Id);
        reloaded1!.Status.Should().Be(BookingStatus.Accepted);

        // Booking 2 should be Rejected with next slot information
        var reloaded2 = await _db.Bookings.FindAsync(booking2.Id);
        reloaded2!.Status.Should().Be(BookingStatus.Rejected);
        reloaded2.RejectionReason.Should().Contain("16:30");

        // SignalR SlotTakenOfferNext should be sent to customer 2
        _clientProxyMock.Verify(c => c.SendCoreAsync(
            "SlotTakenOfferNext",
            It.Is<object[]>(args => args.Length == 1 && ((SlotTakenOfferDto)args[0]).NextAvailableSlot == new TimeOnly(16, 30)),
            default), Times.Once);

        // In-app SlotContested notification sent to customer 2
        _notificationsMock.Verify(n => n.NotifyAsync(
            _customer2.Id,
            NotificationType.SlotContested,
            It.IsAny<string>(),
            It.Is<string>(msg => msg.Contains("16:30")),
            "/Bookings/MyBookings"), Times.Once);
    }

    [Fact]
    public async Task UpdateStatusAsync_BarberRejectsPrimary_PromotesNextInQueue()
    {
        // Arrange: 2 competing pending bookings
        var date = DateOnly.FromDateTime(DateTime.Today.AddDays(2));
        var start = new TimeOnly(17, 0);
        var end = new TimeOnly(17, 30);

        var booking1 = new Booking
        {
            Id = Guid.NewGuid(),
            UserId = _customer1.Id,
            BarberId = _barber.Id,
            ServiceId = _service.Id,
            AppointmentDate = date,
            StartTime = start,
            EndTime = end,
            Status = BookingStatus.Pending,
            CreatedAt = DateTime.UtcNow.AddMinutes(-10)
        };

        var booking2 = new Booking
        {
            Id = Guid.NewGuid(),
            UserId = _customer2.Id,
            BarberId = _barber.Id,
            ServiceId = _service.Id,
            AppointmentDate = date,
            StartTime = start,
            EndTime = end,
            Status = BookingStatus.Pending,
            CreatedAt = DateTime.UtcNow.AddMinutes(-5)
        };

        _db.Bookings.AddRange(booking1, booking2);
        await _db.SaveChangesAsync();

        // Act: Barber rejects booking1
        var updateResult = await _sut.UpdateStatusAsync(_barber.Id, new UpdateBookingStatusDto
        {
            BookingId = booking1.Id,
            NewStatus = BookingStatus.Rejected,
            RejectionReason = "Barber busy"
        });

        // Assert
        updateResult.IsSuccess.Should().BeTrue();

        // Customer 2 should receive SlotPromoted notification
        _notificationsMock.Verify(n => n.NotifyAsync(
            _customer2.Id,
            NotificationType.SlotPromoted,
            "You're #1 in Line! 🚀",
            It.IsAny<string>(),
            "/Bookings/MyBookings"), Times.Once);
    }

    [Fact]
    public async Task CancelAsync_UserCancelsBooking_PromotesNextQueuedBooking()
    {
        // Arrange
        var date = DateOnly.FromDateTime(DateTime.Today.AddDays(2));
        var start = new TimeOnly(18, 0);
        var end = new TimeOnly(18, 30);

        var booking1 = new Booking
        {
            Id = Guid.NewGuid(),
            UserId = _customer1.Id,
            BarberId = _barber.Id,
            ServiceId = _service.Id,
            AppointmentDate = date,
            StartTime = start,
            EndTime = end,
            Status = BookingStatus.Pending,
            CreatedAt = DateTime.UtcNow.AddMinutes(-10)
        };

        var booking2 = new Booking
        {
            Id = Guid.NewGuid(),
            UserId = _customer2.Id,
            BarberId = _barber.Id,
            ServiceId = _service.Id,
            AppointmentDate = date,
            StartTime = start,
            EndTime = end,
            Status = BookingStatus.Pending,
            CreatedAt = DateTime.UtcNow.AddMinutes(-5)
        };

        _db.Bookings.AddRange(booking1, booking2);
        await _db.SaveChangesAsync();

        // Act: Customer 1 cancels booking
        var cancelResult = await _sut.CancelAsync(_customer1.Id, booking1.Id);

        // Assert
        cancelResult.IsSuccess.Should().BeTrue();

        var reloaded1 = await _db.Bookings.FindAsync(booking1.Id);
        reloaded1!.Status.Should().Be(BookingStatus.Cancelled);

        // Customer 2 promoted
        _notificationsMock.Verify(n => n.NotifyAsync(
            _customer2.Id,
            NotificationType.SlotPromoted,
            "Slot Opened Up! 🚀",
            It.IsAny<string>(),
            "/Bookings/MyBookings"), Times.Once);
    }

    [Fact]
    public async Task ShiftSlotAsync_ContestedCustomerShiftsToNextSlot_UpdatesBookingToNewSlot()
    {
        // Arrange: Customer 2 booking was rejected/contested
        var oldDate = DateOnly.FromDateTime(DateTime.Today.AddDays(2));
        var newDate = DateOnly.FromDateTime(DateTime.Today.AddDays(2));
        var oldStart = new TimeOnly(16, 0);
        var newStart = new TimeOnly(16, 30);

        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            UserId = _customer2.Id,
            BarberId = _barber.Id,
            ServiceId = _service.Id,
            AppointmentDate = oldDate,
            StartTime = oldStart,
            EndTime = new TimeOnly(16, 30),
            Status = BookingStatus.Rejected,
            RejectionReason = "Slot booked by another customer. Next available: 16:30"
        };
        _db.Bookings.Add(booking);
        await _db.SaveChangesAsync();

        // Act: Customer 2 shifts to 16:30
        var shiftResult = await _sut.ShiftSlotAsync(_customer2.Id, new ShiftBookingSlotDto
        {
            BookingId = booking.Id,
            NewAppointmentDate = newDate,
            NewStartTime = newStart
        });

        // Assert
        shiftResult.IsSuccess.Should().BeTrue();

        var reloaded = await _db.Bookings.FindAsync(booking.Id);
        reloaded!.Status.Should().Be(BookingStatus.Pending);
        reloaded.StartTime.Should().Be(newStart);
        reloaded.EndTime.Should().Be(new TimeOnly(17, 0));
        reloaded.RejectionReason.Should().BeNull();

        // Barber should be notified about rescheduled booking
        _notificationsMock.Verify(n => n.NotifyAsync(
            _barber.Id,
            NotificationType.BookingCreated,
            "Booking Rescheduled ⚡",
            It.IsAny<string>(),
            "/Bookings/Manage"), Times.Once);
    }

    [Fact]
    public async Task UpdateStatusAsync_BarberMarksCompleted_UpdatesStatusAndNotifiesUser()
    {
        // Arrange
        var date = DateOnly.FromDateTime(DateTime.Today);
        var start = new TimeOnly(14, 0);
        var end = new TimeOnly(14, 30);

        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            UserId = _customer1.Id,
            BarberId = _barber.Id,
            ServiceId = _service.Id,
            AppointmentDate = date,
            StartTime = start,
            EndTime = end,
            Status = BookingStatus.Accepted,
            TotalAmount = 350
        };
        _db.Bookings.Add(booking);
        await _db.SaveChangesAsync();

        // Act
        var result = await _sut.UpdateStatusAsync(_barber.Id, new UpdateBookingStatusDto
        {
            BookingId = booking.Id,
            NewStatus = BookingStatus.Completed
        });

        // Assert
        result.IsSuccess.Should().BeTrue();

        var reloaded = await _db.Bookings.FindAsync(booking.Id);
        reloaded!.Status.Should().Be(BookingStatus.Completed);

        // User should receive BookingCompleted notification
        _notificationsMock.Verify(n => n.NotifyAsync(
            _customer1.Id,
            NotificationType.BookingCompleted,
            "Appointment Completed ✂️",
            It.IsAny<string>(),
            "/Bookings/MyBookings"), Times.Once);
    }
}
