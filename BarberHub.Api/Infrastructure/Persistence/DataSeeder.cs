using BarberHub.Api.Domain.Constants;
using BarberHub.Api.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BarberHub.Api.Infrastructure.Persistence;

public static class DataSeeder
{
    public static async Task SeedAsync(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager)
    {
        await context.Database.MigrateAsync();

        // 1. Seed Roles
        foreach (var role in AppRoles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }

        // 2. Seed Barbers
        var barberDefinitions = new[]
        {
            new
            {
                Email = "barber@barberhub.com",
                FullName = "John's Barber Shop",
                ShopName = "John's Premium Barber Shop",
                ShopDescription = "Premium grooming for modern gentlemen. 15+ years of experience.",
                Phone = "9999999999",
                City = "Pune",
                Address = "123 MG Road, Camp, Pune - 411001",
                Lat = 18.5204,
                Lng = 73.8567,
                Holiday = DayOfWeek.Monday,
                Open = new TimeOnly(9, 0),
                Close = new TimeOnly(20, 0),
                Password = "Admin@123"
            },
            new
            {
                Email = "alex@barberhub.com",
                FullName = "Alex Morgan",
                ShopName = "Alex's Grooming Lounge",
                ShopDescription = "Specialized in fade cuts, beard styling, and hot towel shaves.",
                Phone = "9988776655",
                City = "Pune",
                Address = "Shop 4, North Main Road, Koregaon Park, Pune - 411001",
                Lat = 18.5362,
                Lng = 73.8940,
                Holiday = DayOfWeek.Tuesday,
                Open = new TimeOnly(10, 0),
                Close = new TimeOnly(21, 0),
                Password = "Admin@123"
            },
            new
            {
                Email = "royalfade@barberhub.com",
                FullName = "Vikram Malhotra",
                ShopName = "Royal Fade Barbershop",
                ShopDescription = "Modern hair designs, hair treatments, and VIP grooming packages.",
                Phone = "9977665544",
                City = "Pune",
                Address = "Plot 12, Paud Road, Kothrud, Pune - 411038",
                Lat = 18.5074,
                Lng = 73.8077,
                Holiday = DayOfWeek.Wednesday,
                Open = new TimeOnly(9, 30),
                Close = new TimeOnly(20, 30),
                Password = "Admin@123"
            },
            new
            {
                Email = "bladebrush@barberhub.com",
                FullName = "Siddharth Rao",
                ShopName = "Blade & Brush Salon",
                ShopDescription = "High-end luxury salon & spa for men and women.",
                Phone = "9966554433",
                City = "Pune",
                Address = "Phoenix Marketcity Area, Viman Nagar, Pune - 411014",
                Lat = 18.5679,
                Lng = 73.9143,
                Holiday = DayOfWeek.Monday,
                Open = new TimeOnly(10, 0),
                Close = new TimeOnly(22, 0),
                Password = "Admin@123"
            }
        };

        var seededBarbers = new List<ApplicationUser>();

        foreach (var def in barberDefinitions)
        {
            var barberUser = await userManager.FindByEmailAsync(def.Email);
            if (barberUser is null)
            {
                barberUser = new ApplicationUser
                {
                    UserName = def.Email,
                    Email = def.Email,
                    EmailConfirmed = true,
                    PhoneNumber = def.Phone,
                    PhoneNumberConfirmed = true,
                    FullName = def.FullName,
                    ShopName = def.ShopName,
                    ShopDescription = def.ShopDescription,
                    City = def.City,
                    Address = def.Address,
                    Latitude = def.Lat,
                    Longitude = def.Lng,
                    WeeklyHoliday = def.Holiday,
                    OpeningTime = def.Open,
                    ClosingTime = def.Close,
                    ApprovalStatus = BarberApprovalStatus.Approved,
                    ApprovedAt = DateTime.UtcNow,
                    IsActive = true
                };

                var result = await userManager.CreateAsync(barberUser, def.Password);
                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(barberUser, AppRoles.Admin);
                }
            }
            else
            {
                barberUser.ApprovalStatus = BarberApprovalStatus.Approved;
                barberUser.IsActive = true;
                await userManager.UpdateAsync(barberUser);
                await userManager.RemovePasswordAsync(barberUser);
                await userManager.AddPasswordAsync(barberUser, def.Password);
                if (!await userManager.IsInRoleAsync(barberUser, AppRoles.Admin))
                {
                    await userManager.AddToRoleAsync(barberUser, AppRoles.Admin);
                }
            }
            seededBarbers.Add(barberUser);
        }

        // 3. Seed Customers
        var customerDefinitions = new[]
        {
            new { Email = "user@barberhub.com", FullName = "Demo User", Phone = "8888888888", Password = "User@123", City = "Pune", Lat = 18.5304, Lng = 73.8467 },
            new { Email = "user1@barberhub.com", FullName = "Rahul Sharma", Phone = "9820011111", Password = "User@123", City = "Pune", Lat = 18.5200, Lng = 73.8500 },
            new { Email = "user2@barberhub.com", FullName = "Sneha Patil", Phone = "9820022222", Password = "User@123", City = "Pune", Lat = 18.5350, Lng = 73.8900 },
            new { Email = "user3@barberhub.com", FullName = "Amit Verma", Phone = "9820033333", Password = "User@123", City = "Pune", Lat = 18.5100, Lng = 73.8100 },
            new { Email = "user4@barberhub.com", FullName = "Priya Deshmukh", Phone = "9820044444", Password = "User@123", City = "Pune", Lat = 18.5600, Lng = 73.9100 }
        };

        foreach (var def in customerDefinitions)
        {
            var custUser = await userManager.FindByEmailAsync(def.Email);
            if (custUser is null)
            {
                custUser = new ApplicationUser
                {
                    UserName = def.Email,
                    Email = def.Email,
                    EmailConfirmed = true,
                    PhoneNumber = def.Phone,
                    PhoneNumberConfirmed = true,
                    FullName = def.FullName,
                    City = def.City,
                    Latitude = def.Lat,
                    Longitude = def.Lng,
                    ApprovalStatus = BarberApprovalStatus.NotApplicable,
                    IsActive = true
                };
                var result = await userManager.CreateAsync(custUser, def.Password);
                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(custUser, AppRoles.User);
                }
            }
            else
            {
                custUser.IsActive = true;
                await userManager.UpdateAsync(custUser);
                await userManager.RemovePasswordAsync(custUser);
                await userManager.AddPasswordAsync(custUser, def.Password);
                if (!await userManager.IsInRoleAsync(custUser, AppRoles.User))
                {
                    await userManager.AddToRoleAsync(custUser, AppRoles.User);
                }
            }
        }

        // 4. Seed SuperAdmin user
        var superAdminEmail = "superadmin@barberhub.com";
        var superAdmin = await userManager.FindByEmailAsync(superAdminEmail);
        if (superAdmin is null)
        {
            superAdmin = new ApplicationUser
            {
                UserName = superAdminEmail,
                Email = superAdminEmail,
                EmailConfirmed = true,
                PhoneNumber = "7777777777",
                PhoneNumberConfirmed = true,
                FullName = "Platform Super Admin",
                City = "Pune",
                IsActive = true
            };
            var result = await userManager.CreateAsync(superAdmin, "SuperAdmin@123");
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(superAdmin, AppRoles.SuperAdmin);
            }
        }
        else
        {
            await userManager.RemovePasswordAsync(superAdmin);
            await userManager.AddPasswordAsync(superAdmin, "SuperAdmin@123");
            if (!await userManager.IsInRoleAsync(superAdmin, AppRoles.SuperAdmin))
            {
                await userManager.AddToRoleAsync(superAdmin, AppRoles.SuperAdmin);
            }
        }

        // 5. Seed Services for all barbers
        foreach (var b in seededBarbers)
        {
            var hasServices = await context.Services.AnyAsync(s => s.BarberId == b.Id);
            if (!hasServices)
            {
                var services = new List<Service>
                {
                    new() { Name = "Haircut", Description = "Classic men's haircut with styling", Price = 300, DurationMinutes = 30, BarberId = b.Id, Category = "Hair", IsActive = true },
                    new() { Name = "Beard Trim", Description = "Professional beard trimming and shaping", Price = 200, DurationMinutes = 20, BarberId = b.Id, Category = "Beard", IsActive = true },
                    new() { Name = "Haircut + Beard", Description = "Complete grooming package", Price = 450, DurationMinutes = 45, BarberId = b.Id, Category = "Package", IsActive = true },
                    new() { Name = "Hair Color", Description = "Hair coloring service", Price = 1200, DurationMinutes = 90, BarberId = b.Id, Category = "Color", IsActive = true },
                    new() { Name = "Head Massage", Description = "Relaxing head and shoulder massage", Price = 400, DurationMinutes = 30, BarberId = b.Id, Category = "Massage", IsActive = true },
                    new() { Name = "Face Cleanup", Description = "Deep cleansing and exfoliating facial", Price = 500, DurationMinutes = 30, BarberId = b.Id, Category = "Facial", IsActive = true }
                };
                await context.Services.AddRangeAsync(services);
            }
        }
        await context.SaveChangesAsync();

        // 6. Seed Products for first barber
        var firstBarber = seededBarbers.FirstOrDefault();
        if (firstBarber is not null && !await context.Products.AnyAsync())
        {
            var products = new List<Product>
            {
                new() { Name = "Premium Hair Wax", Description = "Strong hold hair wax for all-day styling", Price = 450, CostPrice = 240, StockQuantity = 50, Category = "Styling", BarberId = firstBarber.Id, DiscountPercentage = 10 },
                new() { Name = "Beard Oil", Description = "Natural beard oil for softness and shine", Price = 550, CostPrice = 290, StockQuantity = 40, Category = "Beard Care", BarberId = firstBarber.Id },
                new() { Name = "Hair Shampoo", Description = "Anti-dandruff shampoo 250ml", Price = 320, CostPrice = 170, StockQuantity = 100, Category = "Hair Care", BarberId = firstBarber.Id, DiscountPercentage = 15 },
                new() { Name = "Aftershave Lotion", Description = "Soothing aftershave with menthol", Price = 380, CostPrice = 200, StockQuantity = 60, Category = "Shaving", BarberId = firstBarber.Id },
                new() { Name = "Hair Pomade", Description = "Classic pomade for a sleek look", Price = 650, CostPrice = 350, StockQuantity = 30, Category = "Styling", BarberId = firstBarber.Id },
                new() { Name = "Beard Comb", Description = "Wooden beard comb", Price = 200, CostPrice = 90, StockQuantity = 80, Category = "Accessories", BarberId = firstBarber.Id }
            };
            await context.Products.AddRangeAsync(products);
            await context.SaveChangesAsync();
        }
    }
}
