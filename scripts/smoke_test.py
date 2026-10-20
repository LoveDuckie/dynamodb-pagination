"""Run after `dotnet run` and seeding local DB; requires requests + beautifulsoup4."""
from urllib.parse import urljoin, urlparse, parse_qs
import requests
from bs4 import BeautifulSoup

BASE = 'http://localhost:5000'
path = '/users/alice/posts'
seen = []
page_urls = []
for index in range(5):
    url = urljoin(BASE, path)
    response = requests.get(url, timeout=10)
    response.raise_for_status()
    soup = BeautifulSoup(response.text, 'html.parser')
    posts = [h.text.strip() for h in soup.select('article h2')]
    print('Forward page', index + 1, posts)
    seen.extend(posts)
    page_urls.append(url)
    prev = soup.find('a', string=lambda text: text and 'Previous' in text)
    nxt = soup.find('a', string=lambda text: text and 'Next' in text)
    assert (prev is None) == (index == 0), 'Only first page may omit Previous'
    if nxt is None:
        break
    assert 'cursor' in parse_qs(urlparse(nxt['href']).query)
    path = nxt['href']

assert len(seen) == 18 and len(set(seen)) == 18, seen
assert len(page_urls) == 4, page_urls

# Navigate backwards: page 4 -> 3 -> 2 -> 1, expect original first page URL to have no cursor.
url = page_urls[-1]
for index in range(3, 0, -1):
    html = requests.get(url, timeout=10).text
    soup = BeautifulSoup(html, 'html.parser')
    prev = soup.find('a', string=lambda text: text and 'Previous' in text)
    assert prev is not None
    url = urljoin(BASE, prev['href'])
    response = requests.get(url, timeout=10)
    response.raise_for_status()
    print('Backward from page', index + 1, '->', [h.text.strip() for h in BeautifulSoup(response.text, 'html.parser').select('article h2')])

soup = BeautifulSoup(requests.get(url, timeout=10).text, 'html.parser')
assert soup.find('a', string=lambda text: text and 'Previous' in text) is None

# Changing a token fails.
next_href = BeautifulSoup(requests.get(urljoin(BASE, '/users/alice/posts'), timeout=10).text, 'html.parser').find('a', string=lambda text: text and 'Next' in text)['href']
# Flip a Base64Url character in the middle of the protected token, rather than
# at the end (where unused Base64 padding bits could leave bytes unchanged).
from urllib.parse import urlsplit, parse_qs, urlencode, urlunsplit
parts = urlsplit(next_href)
query = parse_qs(parts.query)
token = query['cursor'][0]
i = len(token) // 2
query['cursor'] = token[:i] + ('A' if token[i] != 'A' else 'B') + token[i+1:]
modified = urlunsplit((parts.scheme, parts.netloc, parts.path, urlencode(query, doseq=True), parts.fragment))
assert requests.get(urljoin(BASE, modified), timeout=10).status_code == 400
print('PASS: forward/backward navigation, first-page boundary and cursor tampering')
