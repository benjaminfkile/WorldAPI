var builder = WebApplication.CreateBuilder(args);

// Services

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ---- CORS (DEV ONLY) ----
builder.Services.AddCors(options =>
{
    options.AddPolicy("DevCors", policy =>
    {
        policy
            .WithOrigins("http://localhost:3000")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

// Middleware

app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();

app.UseRouting();

app.UseCors("DevCors");

app.UseAuthorization();

// Enable attribute-routed controllers
app.MapControllers();

app.Run();
