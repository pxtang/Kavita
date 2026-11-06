using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using API.Data;
using API.DTOs.Account;
using API.Entities;
using API.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using Xunit;

namespace API.Tests.Services;

public class RefreshTokenSecurityTests
{
    private const string Key = "backport-regression-test-key-only-0123456789abcdefghijklmnopqrstuvwxyz-0123456789";
    private const string RefreshToken = "valid-refresh-token";
    private readonly AppUser _user = new() { Id = 42, UserName = "test-user" };
    private readonly UserManager<AppUser> _users;
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly TokenService _service;

    public RefreshTokenSecurityTests()
    {
        _users = Substitute.For<UserManager<AppUser>>(
            Substitute.For<IUserStore<AppUser>>(),
            Options.Create(new IdentityOptions()),
            new PasswordHasher<AppUser>(),
            Array.Empty<IUserValidator<AppUser>>(),
            Array.Empty<IPasswordValidator<AppUser>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            Substitute.For<IServiceProvider>(),
            NullLogger<UserManager<AppUser>>.Instance);
        _users.FindByIdAsync("42").Returns(_user);
        _users.FindByNameAsync("test-user").Returns(_user);
        _users.GetRolesAsync(_user).Returns(new List<string> { "Login" });
        _users.VerifyUserTokenAsync(_user, TokenOptions.DefaultProvider, "RefreshToken", RefreshToken).Returns(true);
        _users.GenerateUserTokenAsync(_user, TokenOptions.DefaultProvider, "RefreshToken").Returns("new-refresh-token");
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
        {
            ["TokenKey"] = Key
        }).Build();
        _service = new TokenService(config, _users, NullLogger<TokenService>.Instance, _unitOfWork);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(-1)]
    [InlineData(0)]
    public async Task InvalidRefreshTokenIsRejectedRegardlessOfJwtExpiry(int daysUntilExpiry)
    {
        var result = await Refresh(Jwt(daysUntilExpiry: daysUntilExpiry), "invalid-refresh-token");
        Assert.Null(result);
        await AssertNoTokensIssued();
    }

    [Theory]
    [InlineData(10)]
    [InlineData(-1)]
    [InlineData(0)]
    public async Task ValidSignedJwtAndRefreshTokenCanRefreshIncludingExpiredJwt(int daysUntilExpiry)
    {
        var result = await Refresh(Jwt(daysUntilExpiry: daysUntilExpiry));
        Assert.NotNull(result);
        Assert.False(string.IsNullOrWhiteSpace(result.Token));
        Assert.Equal("new-refresh-token", result.RefreshToken);
        await _users.Received(1).VerifyUserTokenAsync(_user, TokenOptions.DefaultProvider, "RefreshToken", RefreshToken);
    }

    [Fact]
    public async Task JwtIssuedByTheServiceCanRefresh()
    {
        var token = await _service.CreateToken(_user);
        Assert.NotNull(await Refresh(token));
    }

    [Fact]
    public async Task JwtSignedWithAnotherKeyIsRejectedEvenWithValidRefreshToken()
    {
        Assert.Null(await Refresh(Jwt(key: Key + "other-server")));
        await _users.DidNotReceive().FindByIdAsync(Arg.Any<string>());
        await AssertNoTokensIssued();
    }

    [Fact]
    public async Task UnsignedJwtIsRejectedEvenWithValidRefreshToken()
    {
        Assert.Null(await Refresh(Jwt(unsigned: true)));
        await AssertNoTokensIssued();
    }

    [Fact]
    public async Task UnexpectedSigningAlgorithmIsRejected()
    {
        Assert.Null(await Refresh(Jwt(algorithm: SecurityAlgorithms.HmacSha256)));
        await AssertNoTokensIssued();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-jwt")]
    public async Task MissingOrMalformedJwtIsRejected(string token)
    {
        Assert.Null(await Refresh(token));
        await AssertNoTokensIssued();
    }

    [Fact]
    public async Task JwtWithoutUserIdIsRejected()
    {
        Assert.Null(await Refresh(Jwt(includeUserId: false)));
        await AssertNoTokensIssued();
    }

    [Fact]
    public async Task UnknownUserIsRejected()
    {
        Assert.Null(await Refresh(Jwt(userId: "999")));
        await AssertNoTokensIssued();
    }

    [Fact]
    public async Task RefreshTokenBelongingToAnotherUserIsRejected()
    {
        var other = new AppUser { Id = 43, UserName = "another-user" };
        _users.FindByIdAsync("43").Returns(other);
        Assert.Null(await Refresh(Jwt(userId: "43")));
        await _users.Received(1).VerifyUserTokenAsync(other, TokenOptions.DefaultProvider, "RefreshToken", RefreshToken);
        await AssertNoTokensIssued();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task MissingRefreshTokenIsRejected(string refreshToken)
    {
        Assert.Null(await Refresh(Jwt(), refreshToken));
        await AssertNoTokensIssued();
    }

    private Task<TokenRequestDto> Refresh(string token, string refreshToken = RefreshToken)
    {
        return _service.ValidateRefreshToken(new TokenRequestDto { Token = token, RefreshToken = refreshToken });
    }

    private async Task AssertNoTokensIssued()
    {
        await _users.DidNotReceive().GenerateUserTokenAsync(Arg.Any<AppUser>(), Arg.Any<string>(), Arg.Any<string>());
        await _users.DidNotReceive().GetRolesAsync(Arg.Any<AppUser>());
        await _unitOfWork.DidNotReceive().CommitAsync();
    }

    private static string Jwt(int daysUntilExpiry = 10, string key = Key, bool unsigned = false,
        bool includeUserId = true, string userId = "42", string algorithm = SecurityAlgorithms.HmacSha512)
    {
        var claims = new List<Claim> { new(JwtRegisteredClaimNames.Name, "test-user") };
        if (includeUserId) claims.Add(new Claim(ClaimTypes.NameIdentifier, userId));
        var token = new JwtSecurityToken(
            claims: claims,
            notBefore: DateTime.UtcNow.AddDays(-20),
            expires: DateTime.UtcNow.AddDays(daysUntilExpiry),
            signingCredentials: unsigned ? null : new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), algorithm));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
