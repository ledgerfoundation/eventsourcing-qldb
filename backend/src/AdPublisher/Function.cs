using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Amazon.IonDotnet.Builders;
using Amazon.IonDotnet.Tree;
using Amazon.Lambda.Core;
using Amazon.QLDB.Driver;
using Amazon.QLDBSession;
using Amazon.Lambda.APIGatewayEvents;


// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace AdPublisher
{
    public class Function
    {
        static AmazonQLDBSessionConfig amazonQLDBSessionConfig = new AmazonQLDBSessionConfig()
        {
            RetryMode = Amazon.Runtime.RequestRetryMode.Standard
        };

        static IQldbDriver driver = QldbDriver.Builder()
            .WithQLDBSessionConfig(amazonQLDBSessionConfig)
            .WithRetryLogging()
            .WithLedger("advertisementLedger")
            .Build();

        /// <summary>
        /// A simple function that takes a string and does a ToUpper
        /// </summary>
        /// <param name="input"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        public APIGatewayProxyResponse FunctionHandler(APIGatewayProxyRequest input, ILambdaContext context)
        {
            Ad adBody = JsonSerializer.Deserialize<Ad>(input.Body);

            if (input.HttpMethod == "POST")
            {
                try
                {
                    // POPULATE PUBLISHER ID
                    adBody.PublisherId = input.QueryStringParameters["publisher"];

                    // GENERATE NEW AD ID 
                    adBody.Id = GenerateUniqueAdId();

                    Console.WriteLine($"new Ad is: {JsonSerializer.Serialize(adBody, typeof(Ad))}");

                    driver.Execute(t =>
                    {
                        // INSERT AD INTO QLDB
                        var doc = IonLoader.Default.Load(JsonSerializer.Serialize(adBody, typeof(Ad)));
                        t.Execute("INSERT INTO Ads ?", doc);
                    });

                    return new APIGatewayProxyResponse
                    {
                        Body = $"Ad with id '{adBody.Id}' created successfully.",
                        StatusCode = 200
                    };
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                    return new APIGatewayProxyResponse
                    {
                        Body = ex.Message,
                        StatusCode = 500
                    };
                }

            }
            else if (input.HttpMethod == "PATCH")
            {
                
                try
                {
                    // POPULATE PUBLISHER ID AND AD ID
                    adBody.PublisherId = input.QueryStringParameters["publisher"];
                    adBody.Id = input.QueryStringParameters["id"];


                    bool owner = false, exists = false;
                    driver.Execute(t =>
                    {
                        // CHECK IF AD BELONGS TO PUBLISHER
                        var result = t.Execute($"SELECT * FROM Ads WHERE adId = '{adBody.Id}'");

                        foreach (var row in result)
                        {
                            exists = true;

                            Console.WriteLine($"Ad {adBody.Id} exists");

                            if (row.GetField("publisherId").StringValue.CompareTo(adBody.PublisherId) == 0)
                            {
                                Console.WriteLine($"Valid owner. Ad {adBody.Id} belongs to publisher {adBody.PublisherId}");
                                owner = true;
                            }
                        }

                        // IF SO, UPDATE AD
                        if (owner && exists)
                        {
                            t.Execute($"UPDATE Ads SET adTitle = '{adBody.Title}', adDescription = '{adBody.Description}', price = {adBody.Price} WHERE adId = '{adBody.Id}'");
                            Console.WriteLine($"Ad '{adBody.Id}' updated");
                        }
                    });

                    if (!exists)
                    {
                        return new APIGatewayProxyResponse
                        {
                            Body = $"Ad {adBody.Id} does not exist.",
                            StatusCode = 400
                        };
                    }

                    if (!owner)
                    {
                        return new APIGatewayProxyResponse
                        {
                            Body = $"Publisher {adBody.PublisherId} is not the owner of Ad {adBody.Id}",
                            StatusCode = 400
                        };
                    }

                    return new APIGatewayProxyResponse
                    {
                        Body = $"Ad '{adBody.Id}' updated successfully.",
                        StatusCode = 200
                    };
                }
                catch (Exception ex)
                {
                    Console.WriteLine("patching error: " + ex);
                    return new APIGatewayProxyResponse
                    {
                        Body = ex.Message,
                        StatusCode = 500
                    };
                }
            }
            else
            {
                Console.WriteLine($"{input.HttpMethod} is a non-support operation");
                return new APIGatewayProxyResponse
                {
                    Body = $"{input.HttpMethod} operation not supported",
                    StatusCode = 500
                };
            }
        }

        private static string GenerateUniqueAdId()
        {
            return Guid.NewGuid().ToString().Substring(0, 8);
        }
    }
}
