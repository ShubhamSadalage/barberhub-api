using BarberHub.Api.Domain.Entities;
using BarberHub.Api.Features.Notifications;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace BarberHub.Api.Tests.Helpers;

public static class MockHelpers
{
    public static Mock<UserManager<ApplicationUser>> CreateUserManager(List<ApplicationUser>? users = null)
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        var userList = users ?? new List<ApplicationUser>();

        var userManager = new Mock<UserManager<ApplicationUser>>(
            store.Object,
            Mock.Of<IOptions<IdentityOptions>>(),
            Mock.Of<IPasswordHasher<ApplicationUser>>(),
            Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            Mock.Of<ILookupNormalizer>(),
            Mock.Of<IdentityErrorDescriber>(),
            Mock.Of<IServiceProvider>(),
            Mock.Of<ILogger<UserManager<ApplicationUser>>>());

        userManager.Setup(m => m.Users).Returns(userList.AsQueryable());

        userManager.Setup(m => m.FindByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync((string email) => userList.FirstOrDefault(u => string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase)));

        userManager.Setup(m => m.FindByIdAsync(It.IsAny<string>()))
            .ReturnsAsync((string id) => userList.FirstOrDefault(u => u.Id == id));

        userManager.Setup(m => m.GetUsersInRoleAsync(It.IsAny<string>()))
            .ReturnsAsync((string role) => userList);

        return userManager;
    }

    public static Mock<IHubContext<NotificationHub>> CreateHubContext()
    {
        var hub = new Mock<IHubContext<NotificationHub>>();
        var clients = new Mock<IHubClients>();
        var clientProxy = new Mock<IClientProxy>();

        hub.Setup(h => h.Clients).Returns(clients.Object);
        clients.Setup(c => c.User(It.IsAny<string>())).Returns(clientProxy.Object);
        clients.Setup(c => c.All).Returns(clientProxy.Object);

        return hub;
    }

    public static ILogger<T> CreateLogger<T>() => Mock.Of<ILogger<T>>();
}
