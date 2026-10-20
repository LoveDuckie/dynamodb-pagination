"""Seed DynamoDB Local. Requires `pip install boto3`. Never targets live AWS."""
import datetime as dt
import os

import boto3

ENDPOINT = 'http://localhost:8000'
TABLE = 'BlogItems'
client = boto3.client('dynamodb', endpoint_url=ENDPOINT,
                      region_name='eu-west-2',
                      aws_access_key_id='local', aws_secret_access_key='local')


def iso(value):
    return value.isoformat().replace('+00:00', 'Z')


def sort_key(created, post_id):
    # Sort by immutable CreatedAtUtc ascending, then a unique post ID.
    # Match C# expected format: yyyyMMddTHHmmssfffffffZ
    date = created.strftime('%Y%m%dT%H%M%S')
    ticks = f'{created.microsecond:06d}0'
    return f'POST#{date}{ticks}Z#{post_id}'


try:
    client.describe_table(TableName=TABLE)
except client.exceptions.ResourceNotFoundException:
    client.create_table(
        TableName=TABLE,
        AttributeDefinitions=[{'AttributeName': 'PK', 'AttributeType': 'S'},
                              {'AttributeName': 'SK', 'AttributeType': 'S'}],
        KeySchema=[{'AttributeName': 'PK', 'KeyType': 'HASH'},
                   {'AttributeName': 'SK', 'KeyType': 'RANGE'}],
        BillingMode='PAY_PER_REQUEST')
    client.get_waiter('table_exists').wait(TableName=TABLE)

base = dt.datetime(2026, 10, 18, 12, 0, tzinfo=dt.timezone.utc)
for user_id, name in [('alice', 'Alice Morgan'), ('bob', 'Bob Taylor')]:
    client.put_item(TableName=TABLE, Item={
        'PK': {'S': f'USER#{user_id}'},
        'SK': {'S': 'PROFILE'},
        'Handle': {'S': user_id},
        'DisplayName': {'S': name},
        'CreatedAtUtc': {'S': iso(base - dt.timedelta(days=100))},
    })
    for n in range(1, 19):
        created = base - dt.timedelta(hours=n)
        updated = created + (dt.timedelta(minutes=10) if n % 3 == 0 else dt.timedelta())
        post_id = f'post-{n:03d}'
        client.put_item(TableName=TABLE, Item={
            'PK': {'S': f'USER#{user_id}'},
            'SK': {'S': sort_key(created, post_id)},
            'PostId': {'S': post_id},
            'Title': {'S': f'{name}: post {n}'},
            'Excerpt': {'S': f'Example blog post {n} from {name}.'},
            'CreatedAtUtc': {'S': iso(created)},
            'LastUpdatedAtUtc': {'S': iso(updated)},
        })

print('Seeded users alice and bob with 18 posts each (5 posts/page).')
