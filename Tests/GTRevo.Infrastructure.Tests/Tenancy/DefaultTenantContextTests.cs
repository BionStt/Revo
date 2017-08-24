﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using GTRevo.Infrastructure.Core.Tenancy;
using GTRevo.Infrastructure.Tenancy;
using NSubstitute;
using Xunit;

namespace GTRevo.Infrastructure.Tests.Tenancy
{
    public class DefaultTenantContextTests
    {
        private readonly DefaultTenantContext sut;
        private readonly ITenantContextResolver tenantContextResolver;
        private readonly HttpContext httpContext;

        public DefaultTenantContextTests()
        {
            tenantContextResolver = Substitute.For<ITenantContextResolver>();
            httpContext = new HttpContext(new HttpRequest("index", "http://localhost/", ""),
                new HttpResponse(new StringWriter()));

            sut = new DefaultTenantContext(tenantContextResolver, httpContext);
        }

        [Fact]
        public void TenantId_ResolvesTenant()
        {
            ITenant tenant = Substitute.For<ITenant>();
            tenant.Id.Returns(Guid.NewGuid());
            tenantContextResolver.ResolveTenant(httpContext).Returns(tenant);

            Assert.Equal(tenant, sut.Tenant);
        }
    }
}
