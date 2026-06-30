using KEISAN_HRIS_v2.Security;
using OfficeOpenXml;
using MySql.Data.MySqlClient;
using System.Data;
using KEISAN_HRIS_v2.Services.EmployeeProfile;
using KEISAN_HRIS_v2.Services.TimeKeeping;
using KEISAN_HRIS_v2.Services.Payroll;
using KEISAN_HRIS_v2.Services.OtherServices;

var builder = WebApplication.CreateBuilder(args);

// ===============================
// SERVICES
// ===============================
builder.Services.AddControllersWithViews();

// Required for Session
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; // SECURITY: Accept both HTTP and HTTPS
    options.Cookie.SameSite = SameSiteMode.Lax; // SECURITY: Balance between security and functionality
});

// MySQL Connection
//builder.Services.AddTransient<IDbConnection>(sp =>
//    new MySqlConnection(
//        builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddScoped<IDbConnection>(sp =>
{
    var conn = new MySqlConnection(
        builder.Configuration.GetConnectionString("DefaultConnection"));
    conn.Open();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = "SET SESSION sql_mode='STRICT_TRANS_TABLES,NO_ZERO_IN_DATE,NO_ZERO_DATE,ERROR_FOR_DIVISION_BY_ZERO,NO_AUTO_CREATE_USER,NO_ENGINE_SUBSTITUTION'";
    cmd.ExecuteNonQuery();
    return conn;
});

// PDF / Services
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
builder.Services.AddScoped<ChangeScheduleRequestPdfService>();
builder.Services.AddScoped<LeaveRequestPdfService>();
builder.Services.AddScoped<OBRequestPdfService>();
builder.Services.AddScoped<OvertimeRequestPdfService>();
builder.Services.AddScoped<OffsetApplicationRequestPdfService>();
builder.Services.AddScoped<OffsetCreditRequestPdfService>();
builder.Services.AddScoped<UndertimeRequestPdfService>();
builder.Services.AddScoped<WFHRequestPdfService>();
builder.Services.AddScoped<PayrollRegisterPdfService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IAuditTrailService, AuditTrailService>();
builder.Services.AddScoped<IApproverService, ApproverService>();
builder.Services.AddHostedService<LeaveExpiryService>();
builder.Services.AddHostedService<AutoPunchBackgroundService>();
builder.Services.AddScoped<CoePdfService>();
builder.Services.AddScoped<PhilHealthCertificatePdfService>();
builder.Services.AddScoped<SssContributionPdfService>();
builder.Services.AddScoped<PagIbigContributionPdfService>();
builder.Services.AddScoped<Employee201PdfService>();
builder.Services.AddHostedService<NotificationCleanupService>();
builder.Services.AddScoped<LastPayPdfService>();
builder.Services.AddScoped<ThirteenthMonthPdfService>();


// EPPlus License
ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
builder.Services.AddTransient<ReviewDTRService>();

// Bind the EmailSettings galing appsettings.json
builder.Services.Configure<EmailService.EmailSettings>(
    builder.Configuration.GetSection("EmailSettings")
);

// Register EmailService
builder.Services.AddScoped<IEmailService, EmailService>();

var app = builder.Build();

// ===============================
// PIPELINE
// ===============================
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

// 🔐 Session MUST be before authorization
app.UseSession();

// AUTHENTICATION MIDDLEWARE (after UseSession, before UseAuthorization)
app.UseAuthenticationMiddleware();

app.UseAuthorization();

// ===============================
// ROUTING
// ===============================
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Auth}/{action=Login}/{id?}");

app.Run();