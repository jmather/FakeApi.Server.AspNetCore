using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using FakeApi.Server.AspNetCore.Models;

namespace FakeApi.Server.AspNetCore.Services
{
    public interface IUserManager
    {
        User Register(UserInfo userInfo);
        User GetUser(string username);
    }

    public class UserManager : IUserManager
    {
        private readonly ConcurrentDictionary<string, User> _users = new ConcurrentDictionary<string, User>();

        private readonly Config _config;

        private Task _cleanup;

        public UserManager() : this(new Config())
        {
        }
        
        public UserManager(Config config)
        {
            _config = config ?? new Config();
        }

        public User Register(UserInfo userInfo)
        {
            EnsureCleanupTaskIsStarted();
            
            var user = CreateUser();

            while (user == null)
            {
                user = CreateUser();
            }

            user.Password = Guid.NewGuid().ToString();
            user.ExternalId = userInfo.ExternalId ?? Guid.NewGuid().ToString();

            return user;
        }

        public User GetUser(string username)
        {
            if (_users.TryGetValue(username, out var user))
            {
                return user;
            }

            return null;
        }

        private void EnsureCleanupTaskIsStarted()
        {
            if (_cleanup != null && _cleanup.Status == TaskStatus.Running)
            {
                return;
            }

            _cleanup = Task.Run(DoCleanup);
        }

        private async Task DoCleanup()
        {
            await Task.Delay(_config.CleanupInterval);

            foreach (var pair in _users)
            {
                if (pair.Value.LastActivityAt + _config.MaxInactivity < DateTime.Now)
                {
                    _users.TryRemove(pair.Key, out var oldUser);
                }
            }
        }

        private User CreateUser(string username = null)
        {
            username = username ?? Guid.NewGuid().ToString();
            
            var user = new User()
            {
                Username = username,
            };

            return _users.TryAdd(username, user) ? user : null;
        }
        
        public class Config
        {
            public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(15);

            public TimeSpan MaxInactivity { get; set; } = TimeSpan.FromHours(2);
        }
    }
}