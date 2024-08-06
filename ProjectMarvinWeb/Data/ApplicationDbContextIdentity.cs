using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ProjectMarvin.Data
{
	/// <summary>
	/// DBContext for Identity
	/// </summary>
	/// <param name="options"></param>
	public class ApplicationDbContextIdentity(DbContextOptions<ApplicationDbContextIdentity> options) : IdentityDbContext<ApplicationUser>(options)
	{
	}
}
