using System;
using System.Threading.Tasks;
using FakeApi.Server.AspNetCore.Models;
using FakeApi.Server.AspNetCore.Repositories;
using Xunit;

namespace FakeApi.Server.AspNetCore.Test.Repositories
{
    public class UserRepositoryTest
    {
        [Fact]
        public void TestGetUser()
        {
            var repo = new UserRepository();

            var userInfo = new UserInfo
            {
                ExternalId = "foo",
            };

            var user = repo.Register(userInfo);
            
            Assert.Equal(user, repo.Get(user.Username));
            Assert.Null(repo.Get(user.Username + "no"));
        }
        
        [Fact]
        public void TestMultipleAttemptsToGetUsers()
        {
            var config = new UserRepository.Config();
            var callCount = 0;

            config.GuidGenerator = () =>
            {
                callCount++;

                return callCount < 10 ? "a" : new Guid().ToString();
            };
            
            var repo = new UserRepository(config);

            var userInfo = new UserInfo
            {
                ExternalId = "foo",
            };

            var user1 = repo.Register(userInfo);

            // a little wait for a little code coverage... heh.
            Task.Delay(100).Wait();
            
            var user2 = repo.Register(userInfo);
            
            Assert.NotEqual(user1.Username, user2.Username);
            Assert.NotInRange(callCount, 0, 10);
        }

        [Fact]
        public async Task TestAuthentication()
        {
            var repo = new UserRepository();

            var userInfo = new UserInfo
            {
                ExternalId = "foo",
            };

            var user = repo.Register(userInfo);

            Assert.Null(await repo.Authenticate("a", "b"));
            Assert.Null(await repo.Authenticate(user.Username, user.Password + "haha"));
            Assert.Equal(user, await repo.Authenticate(user.Username, user.Password));
        }

        [Fact]
        public async Task TestUserPurging()
        {
            var config = new UserRepository.Config();
            
            config.CleanupInterval = TimeSpan.FromMilliseconds(100);
            config.MaxInactivity = TimeSpan.FromMilliseconds(3000);
            
            var repo = new UserRepository(config);
            
            var userInfo = new UserInfo
            {
                ExternalId = "foo",
            };

            var user = repo.Register(userInfo);

            user.LastActivityAt = DateTime.Now.AddMilliseconds(-300);

            await Task.Delay(500);
            
            Assert.Equal(user, repo.Get(user.Username));

            await Task.Delay(3000);
            
            Assert.Null(repo.Get(user.Username));

            await repo.StopCleanup();
        }
    }
}