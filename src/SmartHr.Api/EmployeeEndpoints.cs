using Microsoft.EntityFrameworkCore;
using SmartHr.Infrastructure;

namespace SmartHr.Api;

public static class EmployeeEndpoints
{
    public static IEndpointRouteBuilder MapEmployeeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints
            .MapGet(
                "/api/employees",
                async (RagDbContext db, CancellationToken ct) =>
                    Results.Ok(await db.Employees.AsNoTracking().OrderBy(e => e.EmployeeNumber).ToListAsync(ct))
            )
            .WithTags("Employees");

        return endpoints;
    }
}