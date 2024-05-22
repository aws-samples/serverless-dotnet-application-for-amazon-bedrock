#!/bin/bash
set -e

cat <<-'EOF'
 ___          _                _     ___        _    _  _       _   
| _ ) ___  __| | _ _  ___  __ | |__ |   \  ___ | |_ | \| | ___ | |_ 
| _ \/ -_)/ _` || '_|/ _ \/ _|| / / | |) |/ _ \|  _|| .` |/ -_)|  _|
|___/\___|\__,_||_|  \___/\__||_\_\ |___/ \___/ \__||_|\_|\___| \__|

EOF

function clean_up {
  echo "\n\nCleaning up...\n"
  rm -rf outputs/
  rm -rf solution/*/bin
  rm -rf solution/*/obj
  rm -rf cdk/*/bin
  rm -rf cdk/*/obj
}

read -p "Which region would you like to deploy the demo to? [us-east-1]: " AWS_REGION
AWS_REGION=${AWS_REGION:-us-east-1}
read -p "Enter an existing S3 bucket name to store deployment package: " DEPLOYMENT_BUCKET
read -p "Enter a name for the CloudFront Stack [bedrock-dotnet-demo]: " CFN_STACK_NAME
CFN_STACK_NAME=${CFN_STACK_NAME:-bedrock-dotnet-demo}
read -p "Enter a prefix for the Cognito Domain name to be created: " COGNITO_DOMAIN_PREFIX
read -p "How many days would you like Cloudwatch to retain Lambda logs [1]: " LOG_RETENTION_DAYS
LOG_RETENTION_DAYS=${LOG_RETENTION_DAYS:-1}
read -p "What will be the test users' password [123456]: " TEST_USERS_TEMP_PASSWORD
TEST_USERS_TEMP_PASSWORD=${TEST_USERS_TEMP_PASSWORD:-123456}
read -p "Would you like to preserve the CloudFormation stack in case of deployment failure (for troubleshooting)? [N]: " PRESERVE_STACK
PRESERVE_STACK=${PRESERVE_STACK:-N}

if [ "$PRESERVE_STACK" == "Y" ] || [ "$PRESERVE_STACK" == "y" ]; then
  ROLLBACK="--disable-rollback"
else
  ROLLBACK=""
fi

if ! $(dotnet --list-sdks | grep -q '8.0.'); then
    echo "\n\nInstalling DotNet 8...\n"
    curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --channel 8.0
fi

echo "\n\nPublishing dotnet packages into outputs/ directory...\n"
dotnet publish solution/bedrock_frontend --framework net8.0 --runtime linux-x64 --no-self-contained --output ./outputs/bedrock_frontend/
dotnet publish solution/bedrock_authorizer --framework net8.0 --runtime linux-x64 --no-self-contained --output ./outputs/bedrock_authorizer/
dotnet publish solution/bedrock_text --framework net8.0 --runtime linux-x64 --no-self-contained --output ./outputs/bedrock_text/
dotnet publish solution/bedrock_rag --framework net8.0 --runtime linux-x64 --no-self-contained --output ./outputs/bedrock_rag/
dotnet publish solution/bedrock_image --framework net8.0 --runtime linux-x64 --no-self-contained --output ./outputs/bedrock_image/

echo "\n\nPublishing custom-resource functions into outputs/ directory...\n"
dotnet publish cdk/CustomResourceAossIndex --framework net8.0 --runtime linux-x64 --no-self-contained --output ./outputs/cfn_custom_resource_aoss_index/
dotnet publish cdk/CustomResourceExampleData --framework net8.0 --runtime linux-x64 --no-self-contained --output ./outputs/cfn_custom_resource_example_data/

echo "\n\nPackaging CloudFormation template...\n"
aws cloudformation package --template-file cfn/stack.yml --s3-bucket $DEPLOYMENT_BUCKET --output-template-file ./outputs/cfn-stack.output.yml

if
  echo "\n\nDeploying CloudFormation template...\n"
  aws cloudformation deploy --template-file ./outputs/cfn-stack.output.yml \
      --stack-name $CFN_STACK_NAME \
      --parameter-overrides CognitoDomainPrefix=$COGNITO_DOMAIN_PREFIX LogRetentionDays=$LOG_RETENTION_DAYS \
      --capabilities CAPABILITY_IAM \
      --region $AWS_REGION \
      $ROLLBACK
then
  set +e

  echo "\n\nFinalizing...\n"
  OUTPUT=$(aws cloudformation describe-stacks --stack-name $CFN_STACK_NAME --region $AWS_REGION --query "Stacks[0].Outputs[]")
  WEB_URL=$(echo $OUTPUT | jq -r '.[] | select(.OutputKey == "WebURL") | .OutputValue')
  COGNITO_USER_POOL=$(echo $OUTPUT | jq -r '.[] | select(.OutputKey == "CognitoUserPool") | .OutputValue')

  echo "\n\nSetting test users passwords...\n"
  aws cognito-idp admin-set-user-password --user-pool-id $COGNITO_USER_POOL --username user1 --password $TEST_USERS_TEMP_PASSWORD --permanent --region $AWS_REGION

  clean_up

  echo "\n\nAll done!"
  echo "Access the demo using: $WEB_URL\n\n"
else
  set +e

  clean_up

  echo "\n\nFailed to deploy the CloudFormation stack. Check AWS console for details!"
fi