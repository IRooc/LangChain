﻿using Docker.DotNet;
using Docker.DotNet.Models;
using LangChain.Abstractions.Schema;
using LangChain.Chains.HelperChains;
using System.Formats.Tar;

namespace LangChain.Extensions.Docker
{
    /// <summary>
    /// 
    /// </summary>
    public class DockerChain : BaseStackableChain
    {
        sealed class SuppressProgress : IProgress<JSONMessage>
        {
            public void Report(JSONMessage value)
            {

            }
        }

        private readonly DockerClient _client;
        
        /// <summary>
        /// 
        /// </summary>
        public string Image { get; }
        
        /// <summary>
        /// 
        /// </summary>
        public string Filename { get; }
        
        /// <summary>
        /// 
        /// </summary>
        public string Command { get; }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="image"></param>
        /// <param name="filename"></param>
        /// <param name="command"></param>
        /// <param name="inputKey"></param>
        /// <param name="outputKey"></param>
        public DockerChain(string image= "python:3", string filename="main.py", string command="python", string inputKey="code",string outputKey="result")
        {
            Image = image;
            Filename = filename;
            Command = command;
            InputKeys = new[] {inputKey};
            OutputKeys = new[] {outputKey};

            using var configuration = new DockerClientConfiguration();
            _client = configuration.CreateClient();
        }

        private static string SanitizeCode(string code)
        {
            if (code.StartsWith("```", StringComparison.Ordinal))
            {
                // remove first and last lines
                var lines = code.Split("\n");
                var res = string.Join("\n", lines[1..^1]);
                return res;
            }
            return code;
        }
        
        /// <summary>
        /// 
        /// </summary>
        /// <param name="values"></param>
        /// <returns></returns>
        protected override async Task<IChainValues> InternalCall(IChainValues values)
        {
            values = values ?? throw new ArgumentNullException(nameof(values));
            
            await _client.Images.CreateImageAsync(new ImagesCreateParameters()
            {
                FromImage = Image
            }, null, new SuppressProgress(), CancellationToken.None).ConfigureAwait(false);


            var code = SanitizeCode(values.Value[InputKeys[0]].ToString() ?? string.Empty);


            var tempDir = Path.GetTempPath();
            tempDir = Path.Combine(tempDir, Guid.NewGuid().ToString());
            var appDir = Path.Combine(tempDir, "app");
            Directory.CreateDirectory(appDir);
            var tempFile = Path.Combine(appDir, Filename);
            await File.WriteAllTextAsync(tempFile, code).ConfigureAwait(false);

            MemoryStream archiveStream = new MemoryStream();
            await TarFile.CreateFromDirectoryAsync(tempDir,archiveStream,false).ConfigureAwait(false);
            archiveStream.Seek(0, SeekOrigin.Begin);
            
            Directory.Delete(tempDir,true);

            var container = await _client.Containers.CreateContainerAsync(new CreateContainerParameters()
            {
               
                Image = Image,
                Cmd = new[] {Command,Filename},
                WorkingDir = "/app",
                
            }).ConfigureAwait(false);

            await _client.Containers.ExtractArchiveToContainerAsync(container.ID, new ContainerPathStatParameters()
            {
                AllowOverwriteDirWithFile = true,
                Path = "/",
            }, archiveStream, CancellationToken.None).ConfigureAwait(false);

            await _client.Containers.StartContainerAsync(container.ID, null).ConfigureAwait(false);

            await _client.Containers.WaitContainerAsync(container.ID).ConfigureAwait(false);

            var logs = await _client.Containers.GetContainerLogsAsync(container.ID,
                false,
                               new ContainerLogsParameters()
                               {
                    ShowStdout = true,
                    ShowStderr = true
                }).ConfigureAwait(false);

            var res = await logs.ReadOutputToEndAsync(CancellationToken.None).ConfigureAwait(false);

            var result = res.stdout+res.stderr;

            await _client.Containers.RemoveContainerAsync(container.ID,
                               new ContainerRemoveParameters()
                               {
                    Force = true
                }).ConfigureAwait(false);

            values.Value[OutputKeys[0]] = result;
            return values;
        }
    }
}