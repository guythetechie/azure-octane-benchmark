namespace functionapp

open Azure.Storage.Blobs
open Azure.Storage.Blobs.Specialized
open Azure.Storage.Sas
open System

[<RequireQualifiedAccess>]
module BlobServiceClient =
    let createUserDelegationKey (client: BlobServiceClient) expiresOn =
        async {
            let! cancellationToken = Async.CancellationToken

            return!
                client.GetUserDelegationKeyAsync(Nullable(), expiresOn, cancellationToken)
                |> Async.AwaitTask
        }

[<RequireQualifiedAccess>]
module BlobContainerClient =
    let getAuthenticatedBlobUri (client: BlobContainerClient) expiresOn blobName =
        async {
            let serviceClient = client.GetParentBlobServiceClient()
            let! delegationKey = BlobServiceClient.createUserDelegationKey serviceClient expiresOn

            let blobClient = client.GetBlobClient(blobName)
            let builder = new BlobUriBuilder(blobClient.Uri)

            builder.Sas <-
                let builder = new BlobSasBuilder()
                builder.BlobContainerName <- client.Name
                builder.Resource <- "b"
                builder.BlobName <- blobName
                builder.ExpiresOn <- expiresOn
                builder.SetPermissions(BlobSasPermissions.Read)
                builder.ToSasQueryParameters(delegationKey, serviceClient.AccountName)

            return builder.ToUri()
        }
