#!/bin/bash

# Start docker image
cd /path/to/docker/folder
docker compose up -d --build

# Start Client
log=true
environment=dev
mask1=3314052B4C000042

/path/to/client/executable --log $log --environment $environment --masks $mask1
