using System.Reflection;
using CampusVenue.Api.Infrastructure.Data;
using FluentAssertions;
using Xunit;

namespace CampusVenue.Tests;

public class ArchitectureTests
{
    [Fact]
    public void ApiAssembly_ShouldLoadSuccessfully()
    {
        // Arrange & Act
        var assembly = typeof(CampusVenueDbContext).Assembly;

        // Assert
        assembly.Should().NotBeNull();
        assembly.GetName().Name.Should().Be("CampusVenue.Api");
    }

    [Fact]
    public void DbContext_ShouldInheritFromMicrosoftEntityFrameworkCore()
    {
        // Arrange
        var dbContextType = typeof(CampusVenueDbContext);

        // Assert
        dbContextType.BaseType.Should().Be(typeof(Microsoft.EntityFrameworkCore.DbContext));
    }
}
