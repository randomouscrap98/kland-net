# Kland (dotnet)
A reimplementation of kland in dotnet, hopefully we don't have to do this again

## Getting started
Kland requires an sqlite database. You run the `create_tables.sql` in "Deploy" in the sqlite db file of your choice, and ensure that the `appsettings.json` points to your new kland database. Note that regular posting is currently disabled, so the database is only used for images.

The default backing store for images is a local folder. This will work for debugging, and can work for servers as well. However, there is also an "s3" backing store, where images can be uploaded/downloaded from s3 (with kland acting as a proxy, the bucket does not need to be public). You'll need to set the appropriate settings in `appsettings.json`, and set "UploadStore" to "s3".

That should be all you need to get a basic instance of kland running, just your kland sqlite database set up and a few settings considerations (check `appsettings.json` for anything that might look off to you). If kland does not run properly for you, please let me know (maybe open an issue) and I will hopefully keep this thing in working order.

## S3
If you're using S3, you'll need a bucket to put all the kland images into. After you have a bucket somewhere you want to use, follow the directions
from AWS for setting up a local "profile" (it's just a file you put secret data in), and set the profile name in `appsettings.json`:

> Create or open the shared AWS credentials file. This file is `~/.aws/credentials` on Linux and macOS systems, and `%USERPROFILE%\.aws\credentials` on Windows.
> 
> Add the following text to the shared AWS credentials file, but replace the example ID and example key with the ones you obtained earlier. Remember to save the file.
>
> ```
> [dotnet-tutorials]
> aws_access_key_id = AKIAIOSFODNN7EXAMPLE
> aws_secret_access_key = wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY```

