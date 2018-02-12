using System;
using System.Globalization;
using System.Linq;
using Autofac;
using Autofac.Core.Registration;
using IFramework.Config;
using IFramework.DependencyInjection;
using IFramework.DependencyInjection.Autofac;
using IFramework.DependencyInjection.Microsoft;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

//using IFramework.DependencyInjection.Microsoft;

namespace IFramework.Test
{
    public interface IA
    {
        IC C { get; }
        string Do();
    }

    public interface IB
    {
        string Id { get; set; }
    }

    public class B : IB
    {
        public B()
        {
            ConstructedCount++;
            Id = DateTime.Now.ToString(CultureInfo.InvariantCulture);
        }

        public static int ConstructedCount { get; set; }
        public string Id { get; set; }
    }

    public class B2 : IB
    {
        public B2()
        {
            Id = DateTime.Now.ToString(CultureInfo.InvariantCulture);
        }

        public string Id { get; set; }
    }

    public class A : IA, IDisposable
    {
        private readonly IObjectProvider _objectProvider;

        public readonly IB B;

        public A(IB b, IC c, IObjectProvider objectProvider)
        {
            ConstructedCount++;
            B = b;
            C = c;
            _objectProvider = objectProvider;
        }

        public static int ConstructedCount { get; private set; }
        public IC C { get; set; }

        public string Do()
        {
            return B.Id + C.Id;
        }

        public void Dispose()
        {
            Console.WriteLine("Disposing");
        }
    }

    public interface IC
    {
        int Id { get; set; }
    }
    public class C:IC
    {
        public C(int id)
        {
            Id = id;
        }

        public int Id { get; set; }
    }


    public class DependencyInjectTest
    {
        IObjectProviderBuilder GetNewBuilder()
        {
            B.ConstructedCount = 0;
            //Configuration.Instance
            //             .UseAutofacContainer();
                         //.UseMicrosoftDependencyInjection();
            return new DependencyInjection.Autofac.ObjectProviderBuilder();
            //return new DependencyInjection.Microsoft.ObjectProviderBuilder();
        }

        [Fact]
        public void GetAllServicesTest()
        {
            var builder = GetNewBuilder();
            builder.Register<IB, B>(ServiceLifetime.Singleton)
                   .Register<IB, B2>();
            var objectProvider = builder.Build();
            var bSet = objectProvider.GetAllServices<IB>();
            Assert.NotNull(bSet);
            Assert.Equal(2, bSet.Count());
            objectProvider.Dispose();
        }

        [Fact]
        public void SameServiceTest()
        {
            var builder = GetNewBuilder();
            builder.Register<IB>(p => new B(), ServiceLifetime.Scoped)
                   .Register<B, B>(ServiceLifetime.Scoped);
            var provider = builder.Build();
            using (var scope = provider.CreateScope(ob => ob.RegisterInstance(typeof(IC), new C(1))))
            {
                var b1 = scope.GetService<IB>();
                var b2 = scope.GetService<IB>();

                Assert.Equal(b1.GetHashCode(), b2.GetHashCode());
            }
        }

        [Fact]
        public void GetRequiredServiceTest()
        {
            var builder = GetNewBuilder();

            builder.Register<IB, B>(ServiceLifetime.Singleton)
                   .Register<IB, B2>("B2");
            var objectProvider = builder.Build();

            var b = objectProvider.GetService<IB>("B");
            Assert.Null(b);

            b = objectProvider.GetService<B2>();
            Assert.Null(b);

            b = objectProvider.GetService<IB>("B2");
            Assert.NotNull(b);

            b = objectProvider.GetRequiredService(typeof(IB)) as IB;
            Assert.NotNull(b);

            Assert.Throws<ComponentNotRegisteredException>(() => objectProvider.GetRequiredService<B2>());
            objectProvider.Dispose();
        }

        [Fact]
        public void OverrideInjectTest()
        {
            var builder = GetNewBuilder();

            builder.Register<IB, B2>(ServiceLifetime.Singleton);
            builder.Register<IB, B>(ServiceLifetime.Singleton);
            var objectProvider = builder.Build();
            var b = objectProvider.GetService<IB>();
            Assert.True(b is B);
        }


        [Fact]
        public void ScopeTest()
        {
            var builder = GetNewBuilder();

            builder.Register<IB, B>(ServiceLifetime.Singleton);
            builder.Register<A, A>();
            var objectProvider = builder.Build();
            Assert.NotNull(objectProvider.GetService<IB>());

            using (var scope = objectProvider.CreateScope(ob => ob.RegisterInstance(typeof(IC), new C(1))))
            {
                scope.GetService<IB>();
                Assert.True(scope.GetService(typeof(A)) is A a && a.C.Id == 1);
            }
            using (var scope = objectProvider.CreateScope(ob => ob.RegisterInstance(typeof(IC), new C(2))))
            {
                scope.GetService<IB>();
                var a = scope.GetService<A>();
                Assert.True(a != null && a.C.Id == 2);
            }

            var b = objectProvider.GetService<IB>();
            objectProvider.Dispose();
            Console.WriteLine($"b: {b.Id}");
            Assert.Equal(1, B.ConstructedCount);
        }
    }
}