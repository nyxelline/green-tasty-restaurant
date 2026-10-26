"""
Fills a freshly deployed (empty) backend with demo data for local testing:
one location, 6 tables, 10 dishes (one of them "On Stop") and, optionally,
a waiter. Safe to run again: every item has a fixed id and is overwritten.

Usage (from the backend folder, with the .venv active):
    python ..\\..\\infra\\dev-seed\\seed_dev_data.py
    python ..\\..\\infra\\dev-seed\\seed_dev_data.py --owner-email you+owner@gmail.com --waiter-email you+waiter@gmail.com

With --owner-email / --waiter-email the script also creates those users in
Cognito if they do not exist yet (confirmed, no email needed). It asks for
their password in the console (hidden), so the password never appears in a
chat or a command line. The waiter is put into the Cognito group "Waiter"
and linked to the location. These are TEST accounts: use a throwaway password.
"""
import argparse
import getpass
import sys
from datetime import datetime, timezone
from decimal import Decimal

import boto3
from botocore.exceptions import ClientError

REGION = "eu-north-1"
LOCATION_ID = "11111111-1111-4111-8111-000000000001"

DISH_TYPE = {"Appetizer": 0, "MainCourse": 1, "Dessert": 2}
DISH_STATE = {"OnStop": 0, "Available": 1, "OutOfStock": 2}

LOCATION = {
    "id": LOCATION_ID,
    "address": "48 Rustaveli Avenue, Kyiv",
    "description": "Green & Tasty demo restaurant: seasonal vegetable menu, bright hall, terrace.",
    "totalCapacity": 40,
    "averageOccupancy": "45%",
    "rating": Decimal("4.6"),
}

TABLES = [  # (number, capacity)
    (1, 2), (2, 2), (3, 4), (4, 4), (5, 6), (6, 8),
]

DISHES = [  # (id suffix, name, description, type, price, weight, kcal, state)
    ("01", "Spring Salad", "Fresh greens, radish, cucumber, herb dressing", "Appetizer", "12", "180 g", "140 kcal", "Available"),
    ("02", "Green Asparagus Salad", "Grilled asparagus, lemon, parmesan", "Appetizer", "12", "190 g", "190 kcal", "Available"),
    ("03", "Pumpkin Cream Soup", "Roasted pumpkin, coconut cream, seeds", "Appetizer", "9", "300 g", "210 kcal", "Available"),
    ("04", "Mushroom Risotto", "Arborio rice, forest mushrooms, thyme", "MainCourse", "19", "320 g", "480 kcal", "Available"),
    ("05", "Grilled Salmon", "Salmon fillet, baby potatoes, dill sauce", "MainCourse", "27", "350 g", "560 kcal", "Available"),
    ("06", "Veggie Burger", "Chickpea patty, avocado, sweet potato fries", "MainCourse", "16", "380 g", "620 kcal", "Available"),
    ("07", "Chicken Teriyaki Bowl", "Rice, chicken, edamame, sesame", "MainCourse", "17", "400 g", "590 kcal", "OnStop"),
    ("08", "Berry Cheesecake", "Cream cheese, forest berries", "Dessert", "8", "140 g", "380 kcal", "Available"),
    ("09", "Chocolate Fondant", "Warm dark chocolate cake, vanilla ice cream", "Dessert", "9", "150 g", "430 kcal", "Available"),
    ("10", "Lemon Sorbet", "Vegan lemon sorbet, mint", "Dessert", "6", "120 g", "110 kcal", "Available"),
]


def find_table(dynamodb_client, base_name):
    """Syndicate may add a prefix/suffix to table names: find ours by name."""
    names = []
    paginator = dynamodb_client.get_paginator("list_tables")
    for page in paginator.paginate():
        names.extend(page["TableNames"])
    exact = [n for n in names if n == base_name]
    if exact:
        return exact[0]
    partial = [n for n in names if base_name.lower() in n.lower()]
    if len(partial) == 1:
        return partial[0]
    sys.exit(f"Could not identify the DynamoDB table for '{base_name}'. Found: {names}")


# Photos live in frontend/restaurant-frontend-app/src/assets/img/dishes
DISH_IMAGES = {
    "01": "spring-salad-v2", "02": "green-asparagus-salad", "03": "pumpkin-cream-soup",
    "04": "mushroom-risotto", "05": "grilled-salmon", "06": "veggie-burger",
    "07": "chicken-teriyaki-bowl", "08": "berry-cheesecake", "09": "chocolate-fondant",
    "10": "lemon-sorbet",
}


# (allergens, diets) per dish; an empty list means "nothing declared".
DISH_TAGS = {
    "01": ([], ["vegetarian", "vegan", "gluten-free", "lactose-free"]),
    "02": (["dairy"], ["vegetarian", "gluten-free"]),
    "03": ([], ["vegetarian", "vegan", "gluten-free", "lactose-free"]),
    "04": (["dairy"], ["vegetarian", "gluten-free"]),
    "05": (["fish", "dairy"], ["gluten-free"]),
    "06": (["gluten", "sesame"], ["vegetarian"]),
    "07": (["soy", "sesame"], ["lactose-free"]),
    "08": (["gluten", "dairy", "eggs"], ["vegetarian"]),
    "09": (["gluten", "dairy", "eggs"], ["vegetarian"]),
    "10": ([], ["vegetarian", "vegan", "gluten-free", "lactose-free"]),
}


def seed_core(dynamodb):
    client = dynamodb.meta.client
    locations = dynamodb.Table(find_table(client, "Locations"))
    tables = dynamodb.Table(find_table(client, "Tables"))
    dishes = dynamodb.Table(find_table(client, "Dishes"))

    dish_ids = [f"22222222-2222-4222-8222-0000000000{suffix}" for suffix, *_ in DISHES]
    locations.put_item(Item={**LOCATION, "specialtyDishIds": dish_ids[3:6]})
    print(f"location: {LOCATION['address']}")

    for number, capacity in TABLES:
        tables.put_item(Item={
            "id": f"33333333-3333-4333-8333-0000000000{number:02d}",
            "locationId": LOCATION_ID,
            "capacity": capacity,
            "tableNumber": number,
        })
    print(f"tables: {len(TABLES)}")

    for (suffix, name, desc, dtype, price, weight, kcal, state) in DISHES:
        dishes.put_item(Item={
            "id": f"22222222-2222-4222-8222-0000000000{suffix}",
            "name": name,
            "description": desc,
            "dishType": DISH_TYPE[dtype],
            "price": Decimal(price),
            "weight": weight,
            "calories": kcal,
            "proteins": "10 g",
            "fats": "8 g",
            "carbohydrates": "35 g",
            "vitamins": "A, C",
            "state": DISH_STATE[state],
            "imageUrl": f"/assets/img/dishes/{DISH_IMAGES[suffix]}.jpg",
            "allergens": DISH_TAGS[suffix][0],
            "diets": DISH_TAGS[suffix][1],
            "locationId": LOCATION_ID,
        })
    print(f"dishes: {len(DISHES)} (Spring Salad .. Lemon Sorbet; 'Chicken Teriyaki Bowl' is On Stop)")


def find_user_pool():
    cognito = boto3.client("cognito-idp", region_name=REGION)
    pools = cognito.list_user_pools(MaxResults=20)["UserPools"]
    candidates = [pool for pool in pools if "restaurant" in pool["Name"].lower()] or pools
    if len(candidates) != 1:
        sys.exit(f"Expected exactly one Cognito user pool, found: {[pool['Name'] for pool in candidates]}")
    return cognito, candidates[0]["Id"]


def ensure_group(cognito, pool_id, group_name):
    try:
        cognito.get_group(UserPoolId=pool_id, GroupName=group_name)
    except ClientError as error:
        if error.response["Error"]["Code"] != "ResourceNotFoundException":
            raise
        cognito.create_group(
            UserPoolId=pool_id, GroupName=group_name, Description=f"Group for role {group_name}"
        )


def ensure_user(cognito, pool_id, email, first_name, last_name, password, group_name):
    """Creates a confirmed user if needed and makes sure it is in the group. Returns the user's sub."""
    try:
        user = cognito.admin_get_user(UserPoolId=pool_id, Username=email)
        print(f"user exists: {email}")
        attributes = user["UserAttributes"]
    except ClientError as error:
        if error.response["Error"]["Code"] != "UserNotFoundException":
            raise
        if not password:
            sys.exit(f"User {email} does not exist and no password was provided.")
        created = cognito.admin_create_user(
            UserPoolId=pool_id,
            Username=email,
            MessageAction="SUPPRESS",
            UserAttributes=[
                {"Name": "email", "Value": email},
                {"Name": "email_verified", "Value": "true"},
                {"Name": "given_name", "Value": first_name},
                {"Name": "family_name", "Value": last_name},
            ],
        )
        try:
            cognito.admin_set_user_password(
                UserPoolId=pool_id, Username=email, Password=password, Permanent=True
            )
        except ClientError as error:
            cognito.admin_delete_user(UserPoolId=pool_id, Username=email)
            sys.exit(f"Password was rejected by the user pool policy: {error.response['Error']['Message']}")
        print(f"user created: {email}")
        attributes = created["User"]["Attributes"]

    ensure_group(cognito, pool_id, group_name)
    cognito.admin_add_user_to_group(UserPoolId=pool_id, Username=email, GroupName=group_name)
    return next(item["Value"] for item in attributes if item["Name"] == "sub")


def seed_waiter(dynamodb, cognito, pool_id, email, password):
    sub = ensure_user(cognito, pool_id, email, "Test", "Waiter", password, "Waiter")
    waiters = dynamodb.Table(find_table(dynamodb.meta.client, "Waiters"))
    waiters.put_item(Item={
        "userId": sub,
        "locationId": LOCATION_ID,
        "createdAt": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%S.%fZ"),
        "reservations": [],
    })
    print(f"waiter: {email} (sub {sub}) linked to the location")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--owner-email", help="test customer (table owner) to create if missing")
    parser.add_argument("--waiter-email", help="test waiter to create if missing and link to the location")
    args = parser.parse_args()

    dynamodb = boto3.resource("dynamodb", region_name=REGION)
    seed_core(dynamodb)

    if args.owner_email or args.waiter_email:
        cognito, pool_id = find_user_pool()
        password = getpass.getpass("Password for the test users (hidden, not stored): ")
        if args.owner_email:
            ensure_user(cognito, pool_id, args.owner_email.strip(), "Test", "Owner", password, "Customer")
        if args.waiter_email:
            seed_waiter(dynamodb, cognito, pool_id, args.waiter_email.strip(), password)

    print("done")


if __name__ == "__main__":
    main()
