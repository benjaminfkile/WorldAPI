var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

//Middleware
app.UseSwagger();
app.UseSwaggerUI();


app.UseHttpsRedirection();

// Enable attribute-routed controllers
app.MapControllers();

app.Run();
