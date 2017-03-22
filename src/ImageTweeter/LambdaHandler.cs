using System.Linq;
using System.Collections.Generic;
using System.IO;
using Amazon.Lambda.Core;
using Amazon.Lambda.S3Events;
using Amazon.S3;
using S3Model = Amazon.S3.Model;
using Amazon.Rekognition;
using RekognitionModel = Amazon.Rekognition.Model;
using Tweetinvi;
using TweetinviModels = Tweetinvi.Models;
using Tweetinvi.Parameters;
using System.Threading;
using System.Threading.Tasks;
using ImageSharp;

[assembly:LambdaSerializerAttribute(typeof(Amazon.Lambda.Serialization.Json.JsonSerializer))]
namespace ImageTweeter {
    public class LambdaHandler {

        private IAmazonS3 s3Client;
        private AmazonRekognitionClient rekClient;

        public LambdaHandler() {
            s3Client = new AmazonS3Client();
            rekClient = new AmazonRekognitionClient();
            var consumerKey = "GCNunS5DfXGwh8rvFAterxmXP";
            var consumerSecret = "fq03tBiAIAM7pB6DjRI8S69scCFiR3FibCbjz3HWfjEOPMLSQD";
            var accessToken = "842206967632338944-XMAH3FU86RSak57FVJmglXn4HAvNmpy";
            var accessTokenSecret = "fiFvNoEASqyqWo3FuFr5JKBYyWlILihVLlGCTSxfqAtlv";
            Auth.SetUserCredentials(consumerKey, consumerSecret, accessToken, accessTokenSecret);
        }

        public async Task<string> Handler(S3Event s3Event, ILambdaContext context) {

            // Level 1: Make this output when a file is uploaded to S3
            LambdaLogger.Log("Hello from The AutoMaTweeter!");

            // Level 2: Get the bucket name and key from event data and log to cloudwatch
            var s3 = s3Event.Records.First().S3;
            var bucketName = s3.Bucket.Name;
            var keyName = s3.Object.Key;
            var logmsg = $"bucket: {bucketName}, key: {keyName}";
            LambdaLogger.Log(logmsg);

            // Level 3: Post the image and message to twitter

            // rekognize faces
            var rekFaces = new Amazon.Rekognition.Model.DetectFacesRequest {
                Attributes = new[] { "ALL" }.ToList(),
                Image = new Amazon.Rekognition.Model.Image {
                    S3Object = new Amazon.Rekognition.Model.S3Object {
                        Bucket = bucketName,
                        Name = keyName
                    }
                }
            };
            var rekFacesResult = await rekClient.DetectFacesAsync(rekFaces);
            var emotions = rekFacesResult.FaceDetails
                .SelectMany(fd => fd.Emotions)
                .OrderByDescending(fd => fd.Confidence)
                .Select(e => $"{e.Confidence:N0}% #{e.Type.Value}")
                .First();

            // rekognize labels
            var rekReq = new Amazon.Rekognition.Model.DetectLabelsRequest {
                Image = new Amazon.Rekognition.Model.Image {
                    S3Object = new Amazon.Rekognition.Model.S3Object {
                        Bucket = bucketName,
                        Name = keyName
                    }
                }
            };
            var rekResult = await rekClient.DetectLabelsAsync(rekReq);
            var labels = rekResult.Labels.Select(l => "#" + l.Name).ToArray();
            LambdaLogger.Log($"[{rekResult.HttpStatusCode}] labels({labels.Length}): {string.Join(" ", labels)}");

            var msg = emotions + string.Join($" ", labels);
            using(var response = await s3Client.GetObjectAsync(bucketName, keyName))  {
                var tempFile = Path.GetTempFileName();
                tempFile = Path.ChangeExtension(tempFile, Path.GetExtension(keyName));
                await response.WriteResponseStreamToFileAsync(tempFile, false, default(CancellationToken));
                LambdaLogger.Log($"created '{tempFile}'");

                var image = new Image(File.OpenRead(tempFile));
                var tempFile2 = Path.GetTempFileName();
                tempFile2 = Path.ChangeExtension(tempFile2, Path.GetExtension(keyName));
                image.Lomograph().Grayscale().Save(tempFile2);
                LambdaLogger.Log($"created '{tempFile2}'");

                byte[] fileBytes = File.ReadAllBytes(tempFile2);
                var media = Upload.UploadImage(fileBytes);
                Tweet.PublishTweet($"Team3: {msg}", new PublishTweetOptionalParameters {
                    Medias = new List<TweetinviModels.IMedia> { media }
                });
            }

            // Boss Level: Use Amazon Rekognition to get keywords about the image

            return "Run Complete";
        }
    }
}
