# Kland (dotnet)
A reimplementation of kland in dotnet, hopefully we don't have to do this again

## Getting started
This project serves and uploads images to an S3 bucket. You will need some kind of 
s3 bucket set up for this, there's currently no configuration for an alternative
for testing (this is a quick project).

For s3, after you have a bucket somewhere you want to use, follow the directions
from AWS for setting up a local "profile" (it's just a file you put secret data in):

> Create or open the shared AWS credentials file. This file is ~/.aws/credentials on Linux and macOS systems, and %USERPROFILE%\.aws\credentials on Windows.
> 
> Add the following text to the shared AWS credentials file, but replace the example ID and example key with the ones you obtained earlier. Remember to save the file.
>
> ```
> [dotnet-tutorials]
> aws_access_key_id = AKIAIOSFODNN7EXAMPLE
> aws_secret_access_key = wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY```

