<#
   Date: 07/04/2019
   Purpose: Pre Build Script of Dynamo
#>

docker pull artifactory.dev.adskengineer.net/docker-local-v2/dynamo/buildtools2017sdk81

docker run -m 8GB --cpus=4 -d -t --mount type=bind,source=C:\Jenkins\workspace\Dynamo\Dynamo,target=c:\WorkspaceDynamo --name build-test artifactory.dev.adskengineer.net/docker-local-v2/dynamo/buildtools2017sdk81
