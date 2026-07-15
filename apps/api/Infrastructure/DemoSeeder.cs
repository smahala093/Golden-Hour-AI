using GoldenHour.Api.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace GoldenHour.Api.Infrastructure;

public static class DemoSeeder
{
    public static async Task InitializeAsync(IServiceProvider services, IWebHostEnvironment environment, IConfiguration configuration, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GoldenHourDbContext>();
        if (dbContext.Database.IsInMemory())
        {
            await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        }

        if (environment.IsProduction()) return;
        var password = configuration["Seed:DemoPassword"];
        if (string.IsNullOrWhiteSpace(password)) return;

        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        foreach (var role in new[] { "User", "Caregiver", "EmergencyParticipant", "Administrator" })
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                var result = await roleManager.CreateAsync(new IdentityRole<Guid>(role));
                if (!result.Succeeded) throw new InvalidOperationException($"Could not seed role {role}.");
            }
        }

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var demo = await EnsureUserAsync(userManager, "demo@goldenhour.ai", password, "hi", "User");
        await EnsureUserAsync(userManager, "family@goldenhour.ai", password, "en", "Caregiver");
        if (await dbContext.EmergencyProfiles.AnyAsync(x => x.OwnerId == demo.Id, cancellationToken)) return;

        var profile = new EmergencyProfile
        {
            OwnerId = demo.Id,
            FullName = "Raj Kumar",
            DateOfBirth = new DateOnly(1962, 2, 15),
            PreferredLanguage = "hi",
            ResponseMode = "both",
            BloodGroup = "O+ (self-reported)",
            AllergyStatusCompleted = true,
            MedicationStatusCompleted = true,
            LocationPermissionReviewed = true,
            ReviewedAtUtc = DateTime.UtcNow,
            InsuranceDetails = "Fictional demo insurance record",
            DoctorContact = "Dr Demo, fictional contact",
            Contacts =
            [
                new EmergencyContact { Name = "Asha Kumar", Relationship = "Daughter", PhoneNumber = "+91 90000 00001", IsVerified = true },
                new EmergencyContact { Name = "Vijay Kumar", Relationship = "Son", PhoneNumber = "+91 90000 00002", IsVerified = false }
            ],
            Allergies = [new Allergy { Name = "Penicillin", IsSelfReported = true }],
            Conditions =
            [
                new MedicalCondition { Name = "Hypertension", IsSelfReported = true },
                new MedicalCondition { Name = "Diabetes", IsSelfReported = true }
            ],
            Medications =
            [
                new Medication { Name = "Demo medicine A (fictional; no dosing stored)", IsSelfReported = true },
                new Medication { Name = "Demo medicine B (fictional; no dosing stored)", IsSelfReported = true }
            ],
            Procedures = [new MedicalProcedure { Name = "Angioplasty", Year = 2024, IsSelfReported = true }],
            PreferredHospital = new PreferredHospital { Name = "Golden City Demonstration Hospital", PhoneNumber = "+91 90000 00112" },
            SharingPreference = new SharingPreference
            {
                ShareName = true,
                ShareApproximateAge = true,
                ShareAllergies = true,
                ShareConditions = true,
                ShareMedications = true,
                ShareEmergencyContact = true,
                ReviewedAtUtc = DateTime.UtcNow
            }
        };
        foreach (var contact in profile.Contacts) contact.EmergencyProfileId = profile.Id;
        foreach (var item in profile.Allergies) item.EmergencyProfileId = profile.Id;
        foreach (var item in profile.Conditions) item.EmergencyProfileId = profile.Id;
        foreach (var item in profile.Medications) item.EmergencyProfileId = profile.Id;
        foreach (var item in profile.Procedures) item.EmergencyProfileId = profile.Id;
        profile.PreferredHospital.EmergencyProfileId = profile.Id;
        profile.SharingPreference.EmergencyProfileId = profile.Id;
        dbContext.EmergencyProfiles.Add(profile);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task<ApplicationUser> EnsureUserAsync(UserManager<ApplicationUser> manager, string email, string password, string language, string role)
    {
        var user = await manager.FindByEmailAsync(email);
        if (user is null)
        {
            user = new ApplicationUser { Id = Guid.NewGuid(), UserName = email, Email = email, EmailConfirmed = true, PreferredLanguage = language };
            var result = await manager.CreateAsync(user, password);
            if (!result.Succeeded) throw new InvalidOperationException($"Could not seed demo user {email}: {string.Join(", ", result.Errors.Select(x => x.Code))}");
        }
        if (!await manager.IsInRoleAsync(user, role)) await manager.AddToRoleAsync(user, role);
        return user;
    }
}
