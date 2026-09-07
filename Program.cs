using Microsoft.AspNetCore.RateLimiting;
using MxfaceWebAPI.Common;
using MxfaceWebAPI.Extensions;
using MxfaceWebAPI.Middleware;

// The ABIS master's gRPC endpoint is plain HTTP (h2c), not HTTPS — SocketsHttpHandler refuses
// HTTP/2 over an unencrypted connection unless this is set, before any HttpClient/gRPC channel
// is constructed.
AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddMxfaceFileLogger(builder.Configuration, builder.Environment.ContentRootPath);

// Add services to the container.
builder.Services.AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull);
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddMxfaceCommonServices();
builder.Services.AddApiVersioningSetup();
builder.Services.AddMxfaceSwagger();
builder.Services.AddJwtAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddDefaultAuthorization();
builder.Services.AddMxfaceCors(builder.Configuration);
builder.Services.AddApiRateLimiting();
builder.Services.AddMxfaceGrpcClients(builder.Configuration);
builder.Services.AddMxfaceNpgsqlDataSource(builder.Configuration);
builder.ConfigureMxfaceHosting();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();
app.UseMiddleware<RequestAccessLoggingMiddleware>();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseSecurityHeaders();

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseCors("DefaultCorsPolicy");

app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.UseMxfaceSwagger();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
