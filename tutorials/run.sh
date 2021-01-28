#!/bin/bash

set -e
DIR=$( cd "$(dirname "${BASH_SOURCE[0]}")" ; pwd -P )
$DIR/build.sh
echo "Run the basic tutorial"
docker run -e APPLITOOLS_API_KEY dotnet_basic 
echo "Run the ufg tutorial"
docker run -e APPLITOOLS_API_KEY dotnet_ufg 
$DIR/report.sh