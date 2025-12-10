using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Respsody.Client.Connection;
using Respsody.Client.Connection.Options;
using Respsody.Client.Options;
using Respsody.Resp;
using Respsody.Tests.Library;
using Xunit;
using Xunit.Abstractions;
using static Respsody.Tests.COMMAND;

namespace Respsody.Tests;

public class ReliabilityTests(RedisSingleNodeFixture fixture, ITestOutputHelper @out) : IClassFixture<RedisSingleNodeFixture>
{
    [Theory]
    // [InlineData(50000)]
    [InlineData(100)]
    public async Task ShouldBeProtectedAgainstMultipleDisposals(int iterations)
    {
        var client = await new RespClientFactory()
            .Create(
                new RespClientOptions
                {
                    Handler = new TestHandler(output: @out, logCommandExecuted: false),
                },
                new DefaultConnectionProcedure(new ConnectionOptions
                {
                    AuthOptions = new AuthOptions { Password = fixture.GetPassword() },
                    Endpoint = fixture.GetStringEndpoint()
                }));
        var data = Enumerable.Range(0, 10_000).ToArray();

        for (var iteration = 0; iteration < iterations; iteration++)
        {
            @out.WriteLine($"Iteration #{iteration}");
            await Task.WhenAll(
                data.Select(async i =>
                            {
                                var str = i.ToString();

                                var key = Key.Utf8(str);
                                var value = Value.Utf8(str);

                                await client.Set(key, value);

                                using var result = await client.Get(key);
                                if (!result.ToString(Encoding.UTF8)!.SequenceEqual(str))
                                    throw new Exception($"this should not happen // {i} // {str} != {result.AsUnicodeSpan()}");


                                var str1 = i.ToString();
                                var str2 = (-i).ToString();

                                var key1 = Key.Utf8(str1);
                                var key2 = Key.Utf8(str2);

                                var value1 = Value.Utf8(str1);
                                var value2 = Value.Utf8(str2);

                                await client.Mset([(key1, value1), (key2, value2)]);

                                using var mgetResult = await client.Mget([key1, key2]);
                                
                                var v1 = mgetResult[0].ToRespString();
                                var v2 = mgetResult[1].ToRespString();

                                if (!v1.ToString(Encoding.UTF8)!.SequenceEqual(str1))
                                    throw new Exception($"this should not happen // {i} // {str1} != {v1.AsUnicodeSpan()}");

                                if (!v2.ToString(Encoding.UTF8)!.SequenceEqual(str2))
                                    throw new Exception($"this should not happen // {i} // {str2} != {v2.AsUnicodeSpan()}");

                                using var docs = await client.Command(DOCS);

                                // redundant disposals
                                // don't try this at home

                                docs.Dispose();

                                v1.Dispose();
                                v2.Dispose();
                                v1.Dispose();
                                v2.Dispose();
                                result.Dispose();
                                result.Dispose();
                            }));
        }
    }
}