namespace RinkuDemo; 
public interface IApiModule<T> {
    static abstract void Validate();
    static abstract string Name { get; }
    static abstract Task<int> Create(T a);
    static abstract Task<bool> Delete(int id);
    static abstract IAsyncEnumerable<T> GetAll(HttpContext context);
    static abstract Task<T?> GetOne(int id);
    static abstract Task<bool> Update(int id, HttpContext context);
}
public static class ApiModuleExtensions {
    public static void MapModule<TModule, T>(this IEndpointRouteBuilder app) where TModule : IApiModule<T> {
        TModule.Validate();
        var g = app.MapGroup($"/{TModule.Name.ToLower()}");
        g.MapGet("/", (HttpContext context) => TModule.GetAll(context));
        g.MapGet("/{id:int}", async (int id) => {
            var result = await TModule.GetOne(id);
            return result is not null ? Results.Ok(result) : Results.NotFound();
        });
        g.MapPost("/", async (T item) => {
            var id = await TModule.Create(item);
            return Results.Created($"/{TModule.Name.ToLower()}/{id}", item);
        });
        g.MapPut("/{id:int}", async (int id, HttpContext context) => {
            var success = await TModule.Update(id, context);
            return success ? Results.NoContent() : Results.NotFound();
        });
        g.MapDelete("/{id:int}", async (int id) => {
            var success = await TModule.Delete(id);
            return success ? Results.NoContent() : Results.NotFound();
        });
    }
}