using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using FakeApi.Server.AspNetCore.Models;

namespace FakeApi.Server.AspNetCore.Repositories
{
    public class UserRepository : IUserRepository
    {
        private readonly ConcurrentDictionary<string, User> _users = new ConcurrentDictionary<string, User>();

        private readonly Config _config;

        private Task _cleanup;

        public UserRepository() : this(new Config())
        {
        }
        
        public UserRepository(Config config)
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

        public User Get(string username)
        {
            if (_users.TryGetValue(username, out var user))
            {
                return user;
            }

            return null;
        }

        public Task<User> Authenticate(string username, string password)
        {
            var user = Get(username);

            if (user == null || user.Password != password)
            {
                return Task.FromResult<User>(null);
            }

            return Task.FromResult(user);
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
            await Task.Delay(_config.CleanupInterval).ConfigureAwait(false);

            foreach (var pair in _users)
            {
                if (pair.Value.LastActivityAt + _config.MaxInactivity < DateTime.Now)
                {
                    _users.TryRemove(pair.Key, out var oldUser);
                }
            }
        }

        private User CreateUser(string preferredUsername = null)
        {
            var username = preferredUsername ?? Guid.NewGuid().ToString();
            
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