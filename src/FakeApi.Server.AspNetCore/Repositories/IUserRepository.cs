using System.Threading.Tasks;
using FakeApi.Server.AspNetCore.Models;

namespace FakeApi.Server.AspNetCore.Repositories
{
    public interface IUserRepository
    {
        User Register(UserInfo userInfo);
        
        User Get(string username);
        
        Task<User> Authenticate(string username, string password);
    }
}