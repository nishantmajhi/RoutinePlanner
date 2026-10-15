function convert24hrTo12hr(time) {
  let [hour, minute] = time.split(":").map(Number);
  const ampm = hour >= 12 ? "PM" : "AM";
  hour = hour % 12 || 12;
  return `${hour}:${minute.toString().padStart(2, "0")} ${ampm}`;
}

function getAbbreviation(str) {
  const stopWords = ["of", "and"];
  const words = str.split(" ");
  let abbreviation = "";

  for (let word of words) {
    if (word === word.toUpperCase() && word.length > 1) {
      abbreviation += word;
    } else if (!stopWords.includes(word.toLowerCase())) {
      abbreviation += word.charAt(0).toUpperCase();
    }
  }

  return abbreviation;
}
