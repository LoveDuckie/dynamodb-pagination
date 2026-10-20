using BlogLambda.Domain;

namespace BlogLambda.Repositories;

public interface IBlogUserRepository
{
    Task<BlogUser?> GetAsync(string userId, CancellationToken cancellationToken);
}
