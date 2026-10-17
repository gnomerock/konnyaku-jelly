-- Shared words not in the glossary yet that at least 3 different players reported, with each meaning's vote count.
SELECT s.term,
       s.meaning,
       COUNT(*) AS votes,
       (SELECT COUNT(*) FROM submissions x WHERE x.term = s.term) AS reporters
FROM submissions s
WHERE s.term NOT IN (SELECT ja FROM glossary)
GROUP BY s.term, s.meaning
HAVING reporters >= 3
ORDER BY reporters DESC, votes DESC;
