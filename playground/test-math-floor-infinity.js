// Test Math.floor(Infinity) behavior
console.log('Testing Math.floor(Infinity):');
console.log('Math.floor(Infinity) =', Math.floor(Infinity));
console.log('Infinity =', Infinity);
console.log('Math.floor(Infinity) === Infinity:', Math.floor(Infinity) === Infinity);
console.log('Math.floor(Infinity) != Infinity:', Math.floor(Infinity) != Infinity);
console.log('isNaN(Infinity):', isNaN(Infinity));

// This is the check from the test
function checkValue(value){
  console.log('\ncheckValue called with:', value);
  console.log('Math.floor(value):', Math.floor(value));
  console.log('value:', value);
  console.log('Math.floor(value) != value:', Math.floor(value) != value);
  console.log('isNaN(value):', isNaN(value));

  if(Math.floor(value) != value || isNaN(value)){
    throw ("INVALID_INTEGER_VALUE: " + value);
  }
  else{
    return value;
  }
}

try {
  var result = checkValue(Infinity);
  console.log('SUCCESS: checkValue(Infinity) returned', result);
} catch(e) {
  console.log('ERROR:', e);
}
