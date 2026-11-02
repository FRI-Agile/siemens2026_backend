using System.ComponentModel.DataAnnotations;
using Kernel.Auth;
using Kernel.CQRS;
using Kernel.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using RecruitmentModule.Domain;

namespace RecruitmentModule.Features.JobPostFeatures.ListManagedJobPosts;

/// <summary>
/// <c>GET /api/v1/recruitment/job-posts/manage</c>: every job post, drafts and closed ones included, a page at a time,
/// with what editing, closing and reopening need. Hiring managers (and administrators) only, like those features.
/// </summary>
internal sealed class ListManagedJobPostsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder group) =>
        group.MapGroup("/job-posts")
            .RequireStaff(StaffPolicies.HiringManager)
            .MapGet("/manage", static async (
                [Range(0, int.MaxValue)] int? offset,
                [Range(1, ListManagedJobPostsQuery.MaxLimit)] int? limit,
                [StringLength(200)] string? search,
                [AllowedValues(JobPostStatuses.Draft, JobPostStatuses.Published, JobPostStatuses.Closed, null)]
                string? status,
                [AllowedValues(
                    EmploymentTypes.FullTime, EmploymentTypes.PartTime, EmploymentTypes.FixedTerm,
                    EmploymentTypes.WorkPerformanceAgreement, EmploymentTypes.WorkActivityAgreement,
                    EmploymentTypes.StudentWorkAgreement, EmploymentTypes.SelfEmployed, null)]
                string? employmentType,
                IQueryHandler<ListManagedJobPostsQuery, ListManagedJobPostsResponse> handler,
                CancellationToken ct) =>
            {
                ListManagedJobPostsQuery query = new()
                {
                    Offset = offset ?? 0,
                    Limit = limit ?? ListManagedJobPostsQuery.DefaultLimit,
                    Search = search,
                    Status = status,
                    EmploymentType = employmentType,
                };
                return Results.Ok(await handler.HandleAsync(query, ct));
            })
            .WithName("ListManagedJobPosts")
            .Produces<ListManagedJobPostsResponse>(StatusCodes.Status200OK)
            .ProducesLink(StatusCodes.Status200OK, "UpdateJobPost", ("jobPostId", "$response.body#/items/0/id"))
            .ProducesLink(StatusCodes.Status200OK, "CloseJobPost", ("jobPostId", "$response.body#/items/0/id"))
            .ProducesLink(StatusCodes.Status200OK, "ReopenJobPost", ("jobPostId", "$response.body#/items/0/id"));
}
